param(
    [string]$SiteName = "SGRNetGuard",
    [string]$AppPoolName = "SGRNetGuard-AppPool",
    [string]$HostName = "netguard.sungroup.com.vn",
    [string]$PhysicalPath = "C:\inetpub\SGRNetGuardApi",
    [string]$Configuration = "Release",
    [string]$CertificateThumbprint,
    [string]$SqlServer = "",
    [string]$SqlDatabase = "SGRNetGuard",
    [string]$ServiceAccount = "",
    [securestring]$ServiceAccountPassword,
    [switch]$UseCurrentAppPoolIdentity
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Hãy chạy PowerShell bằng quyền Administrator."
    }
}

function Require-Value {
    param(
        [string]$Value,
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Thiếu tham số bắt buộc: $Name"
    }
}

function Set-AppPoolIdentity {
    param(
        [string]$PoolName
    )

    if ($UseCurrentAppPoolIdentity) {
        Write-Host "Giữ nguyên identity hiện tại của App Pool: $PoolName"
        return
    }

    Require-Value -Value $ServiceAccount -Name "ServiceAccount"

    if ($null -eq $ServiceAccountPassword) {
        $ServiceAccountPassword = Read-Host -Prompt "Nhập mật khẩu cho $ServiceAccount" -AsSecureString
    }

    $plainPassword = [System.Net.NetworkCredential]::new("", $ServiceAccountPassword).Password
    if ([string]::IsNullOrWhiteSpace($plainPassword)) {
        throw "Mật khẩu service account không hợp lệ."
    }

    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel -Value @{
        identityType = 3
        userName = $ServiceAccount
        password = $plainPassword
    }

    Write-Host "Đã set App Pool identity: $ServiceAccount"
}

function Ensure-HttpsBinding {
    param(
        [string]$TargetSiteName,
        [string]$TargetHostName,
        [string]$Thumbprint
    )

    Require-Value -Value $Thumbprint -Name "CertificateThumbprint"

    $cleanThumbprint = $Thumbprint.Replace(" ", "").ToUpperInvariant()
    $cert = Get-Item "Cert:\LocalMachine\My\$cleanThumbprint" -ErrorAction SilentlyContinue
    if ($null -eq $cert) {
        throw "Không tìm thấy certificate thumbprint $cleanThumbprint trong Cert:\LocalMachine\My"
    }

    $existingBinding = Get-WebBinding -Name $TargetSiteName -Protocol "https" |
        Where-Object { $_.bindingInformation -eq "*:443:$TargetHostName" }

    if ($null -eq $existingBinding) {
        New-WebBinding -Name $TargetSiteName -Protocol "https" -Port 443 -HostHeader $TargetHostName -IPAddress "*"
        Write-Host "Đã tạo HTTPS binding *:443:$TargetHostName"
    }
    else {
        Write-Host "HTTPS binding đã tồn tại: *:443:$TargetHostName"
    }

    $sslPath = "IIS:\SslBindings\0.0.0.0!443!$TargetHostName"
    if (Test-Path $sslPath) {
        Remove-Item $sslPath -Force
    }

    New-Item $sslPath -Thumbprint $cleanThumbprint -SSLFlags 1 | Out-Null
    Write-Host "Đã gán certificate cho binding HTTPS."
}

Require-Admin
Require-Value -Value $HostName -Name "HostName"
Require-Value -Value $SqlServer -Name "SqlServer"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "Không tìm thấy dotnet SDK/runtime trong PATH."
}

Import-Module WebAdministration

$scriptDir = Split-Path -Parent $PSCommandPath
$projectPath = Join-Path $scriptDir "SGRNetGuard.Api.csproj"
if (-not (Test-Path $projectPath)) {
    throw "Không tìm thấy project file: $projectPath"
}

if (-not (Test-Path $PhysicalPath)) {
    New-Item -Path $PhysicalPath -ItemType Directory -Force | Out-Null
}

Write-Host "Publish API vào: $PhysicalPath"
dotnet publish $projectPath -c $Configuration -o $PhysicalPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish thất bại."
}

$appSettingsPath = Join-Path $PhysicalPath "appsettings.json"
if (-not (Test-Path $appSettingsPath)) {
    throw "Không tìm thấy appsettings.json trong thư mục publish."
}

$raw = Get-Content -Path $appSettingsPath -Raw
$appSettings = $raw | ConvertFrom-Json
$appSettings.ConnectionStrings.SGRNetGuard = "Server=$SqlServer;Database=$SqlDatabase;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;"
$appSettings.Urls = "http://0.0.0.0:5080"
$appSettings | ConvertTo-Json -Depth 20 | Set-Content -Path $appSettingsPath -Encoding UTF8
Write-Host "Đã cập nhật connection string trong appsettings.json"

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-Item "IIS:\AppPools\$AppPoolName" | Out-Null
    Write-Host "Đã tạo App Pool: $AppPoolName"
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""

Set-AppPoolIdentity -PoolName $AppPoolName

if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port 80 -HostHeader $HostName | Out-Null
    Write-Host "Đã tạo Website: $SiteName"
}
else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PhysicalPath
    Write-Host "Đã cập nhật Website hiện có: $SiteName"
}

$httpBinding = Get-WebBinding -Name $SiteName -Protocol "http" |
    Where-Object { $_.bindingInformation -eq "*:80:$HostName" }
if ($null -eq $httpBinding) {
    New-WebBinding -Name $SiteName -Protocol "http" -Port 80 -HostHeader $HostName -IPAddress "*"
    Write-Host "Đã tạo HTTP binding *:80:$HostName"
}

Ensure-HttpsBinding -TargetSiteName $SiteName -TargetHostName $HostName -Thumbprint $CertificateThumbprint

Restart-WebAppPool -Name $AppPoolName
Stop-Website -Name $SiteName
Start-Website -Name $SiteName

Write-Host ""
Write-Host "Deploy hoàn tất."
Write-Host "Site: https://$HostName/"
Write-Host "Health check: https://$HostName/api/health/db"
Write-Host "Lưu ý: App Pool account ($ServiceAccount) cần được DBA cấp quyền vào SQL Server."
