param(
    [string]$ApiUrl = "https://sgrnetguard-backend.onrender.com",
    [int]$IntervalSeconds = 60,
    [switch]$Once,
    [string]$DeviceName = ""
)

$script:siteConfig = $null
$script:siteConfigLoadedAt = [datetime]::MinValue

function Get-SiteContext {
    param([string]$LanIp)

    try {
        if ($null -eq $script:siteConfig -or ((Get-Date) - $script:siteConfigLoadedAt).TotalHours -ge 6) {
            $script:siteConfig = Invoke-RestMethod -Method Get -Uri "$ApiUrl/api/config" -TimeoutSec 10
            $script:siteConfigLoadedAt = Get-Date
        }

        $address = [System.Net.IPAddress]::Parse($LanIp).GetAddressBytes()
        $matchingSites = @()
        foreach ($site in $script:siteConfig.sites) {
            $subnetParts = $site.subnet -split '/', 2
            if ($subnetParts.Count -ne 2) { continue }
            $network = [System.Net.IPAddress]::Parse($subnetParts[0]).GetAddressBytes()
            $prefixLength = [int]$subnetParts[1]
            $fullBytes = [math]::Floor($prefixLength / 8)
            $remainingBits = $prefixLength % 8
            $matches = $true
            for ($index = 0; $index -lt $fullBytes; $index++) {
                if ($address[$index] -ne $network[$index]) { $matches = $false; break }
            }
            if ($matches -and $remainingBits -gt 0) {
                $mask = [byte](255 - [math]::Pow(2, 8 - $remainingBits) + 1)
                $matches = (($address[$fullBytes] -band $mask) -eq ($network[$fullBytes] -band $mask))
            }
            if ($matches) { $matchingSites += $site }
        }
        return $matchingSites | Sort-Object { [int](($_.subnet -split '/', 2)[1]) } -Descending | Select-Object -First 1
    }
    catch {
        # Site resolution is best effort; the API also resolves by subnet.
    }
    return $null
}

function Get-NetworkContext {
    try {
        $configuration = Get-NetIPConfiguration -ErrorAction Stop |
            Where-Object { $_.IPv4Address.IPAddress -and $_.IPv4Address.IPAddress -notmatch '^(127\.|169\.254\.)' } |
            Select-Object -First 1
        if ($null -eq $configuration) { return @{ Type = ""; Signal = $null; LinkSpeed = "" } }

        $adapter = Get-NetAdapter -InterfaceIndex $configuration.InterfaceIndex -ErrorAction SilentlyContinue
        $type = if ($adapter -and $adapter.NdisPhysicalMedium -eq "Native 802.11") { "WiFi" } elseif ($adapter) { "LAN" } else { "" }
        $signal = if ($type -eq "WiFi") { (netsh wlan show interfaces | Select-String "Signal") -replace '.*:\s*', '' -replace '%', '' } else { $null }
        $linkSpeed = if ($type -eq "LAN") { [string]$adapter.LinkSpeed } else { "" }
        return @{ Type = $type; Signal = if ($signal) { [int]$signal } else { $null }; LinkSpeed = $linkSpeed }
    }
    catch {
        return @{ Type = ""; Signal = $null; LinkSpeed = "" }
    }
}

function Get-CurrentCpuPercent {
    try {
        $cpu = (Get-Counter '\Processor(_Total)\% Processor Time' -ErrorAction Stop).CounterSamples.CookedValue
        if ($null -eq $cpu -or $cpu -lt 0) { return 0 }
        return [math]::Round([double]$cpu, 2)
    }
    catch {
        return 0
    }
}

function Get-CurrentRamPercent {
    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $total = [double]$os.TotalVisibleMemorySize
        $free = [double]$os.FreePhysicalMemory
        if ($total -le 0) { return 0 }
        $used = $total - $free
        return [math]::Round(($used / $total) * 100, 2)
    }
    catch {
        return 0
    }
}

function Get-CurrentDiskPercent {
    try {
        $drives = Get-CimInstance Win32_LogicalDisk -Filter "DriveType = 3" -ErrorAction Stop
        $totalGb = 0
        $freeGb = 0

        foreach ($d in $drives) {
            $size = [double]$d.Size
            $free = [double]$d.FreeSpace
            if ($size -gt 0) {
                $totalGb += $size
                $freeGb += $free
            }
        }

        if ($totalGb -le 0) { return 0 }
        $usedPercent = (($totalGb - $freeGb) / $totalGb) * 100
        return [math]::Round($usedPercent, 2)
    }
    catch {
        return 0
    }
}

function Get-PrivateLanIp {
    try {
        $ip = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object { $_.IPAddress -and $_.IPAddress -notmatch '^(127\.|169\.254\.)' } |
            Where-Object { $_.IPAddress -match '^10\.' -or $_.IPAddress -match '^192\.168\.' -or $_.IPAddress -match '^172\.(1[6-9]|2[0-9]|3[0-1])\.' } |
            Select-Object -ExpandProperty IPAddress -First 1

        return [string]$ip
    }
    catch {
        return ""
    }
}

function Get-MacAddress {
    try {
        $mac = Get-CimInstance Win32_NetworkAdapterConfiguration -Filter "IPEnabled = True" -ErrorAction Stop |
            Where-Object { $_.MacAddress } |
            Select-Object -ExpandProperty MacAddress -First 1
        return [string]$mac
    }
    catch {
        return ""
    }
}

function Get-CpuModel {
    try {
        $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Name
        return [string]$cpu
    }
    catch {
        return ""
    }
}

function Get-RamTotalText {
    try {
        $totalBytes = (Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).TotalPhysicalMemory
        if ($null -eq $totalBytes -or [double]$totalBytes -le 0) { return "" }
        return "{0:N0} GB" -f ([double]$totalBytes / 1GB)
    }
    catch {
        return ""
    }
}

function Get-DiskTotalText {
    try {
        $drives = Get-CimInstance Win32_LogicalDisk -Filter "DriveType = 3" -ErrorAction Stop
        $totalBytes = 0

        foreach ($drive in $drives) {
            $totalBytes += [double]$drive.Size
        }

        if ($totalBytes -le 0) { return "" }
        return "{0:N0} GB" -f ([double]$totalBytes / 1GB)
    }
    catch {
        return ""
    }
}

function Get-MainboardName {
    try {
        $board = Get-CimInstance Win32_BaseBoard -ErrorAction Stop | Select-Object -First 1
        if ($null -eq $board) { return "" }
        return [string]$board.Product
    }
    catch {
        return ""
    }
}

function Get-UptimeText {
    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $lastBoot = [datetime]$os.LastBootUpTime
        $uptime = (Get-Date) - $lastBoot
        return $uptime.ToString("d' ngày 'hh' giờ 'mm' phút'")
    }
    catch {
        return ""
    }
}

function Get-WindowsEdition {
    try {
        $caption = (Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).Caption
        if ([string]::IsNullOrWhiteSpace($caption)) { return "" }

        $edition = $caption.Replace("Microsoft ", "").Trim()
        if ($edition -match 'Windows Server') { return $edition }
        if ($edition -match 'Windows') { return $edition }
        return $edition
    }
    catch {
        return ""
    }
}

function Get-WindowsVersionLabel {
    try {
        $release = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -ErrorAction Stop).DisplayVersion
        if ([string]::IsNullOrWhiteSpace($release)) {
            $release = (Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).Version
        }
        return [string]$release
    }
    catch {
        return ""
    }
}

function Test-IsInternalNetwork {
    $override = [Environment]::GetEnvironmentVariable("SGR_NETGUARD_INTERNAL")
    if (-not [string]::IsNullOrWhiteSpace($override)) {
        if ($override -match '^(1|true|yes)$') { return $true }
        if ($override -match '^(0|false|no)$') { return $false }
    }

    try {
        $profiles = Get-NetConnectionProfile -ErrorAction Stop
        if ($profiles | Where-Object {
            $_.Name -eq "SGR-OFFICE" -or
            $_.NetworkCategory -in @("Private", "DomainAuthenticated")
        }) { return $true }
    }
    catch {
        # Continue with address detection when the profile API is unavailable.
    }

    if (-not [string]::IsNullOrWhiteSpace((Get-PrivateLanIp))) {
        return $true
    }

    try {
        return $false
    }
    catch {
        return $true
    }
}

function Send-Heartbeat {
    $device = if ([string]::IsNullOrWhiteSpace($DeviceName)) { [System.Net.Dns]::GetHostName() } else { $DeviceName }
    $cpu = Get-CurrentCpuPercent
    $ram = Get-CurrentRamPercent
    $disk = Get-CurrentDiskPercent
    $lanIp = Get-PrivateLanIp
    $site = if ($lanIp) { Get-SiteContext -LanIp $lanIp } else { $null }
    $isInternal = $null -ne $site
    $network = Get-NetworkContext
    $macAddress = Get-MacAddress
    $cpuModel = Get-CpuModel
    $ramTotal = Get-RamTotalText
    $diskTotal = Get-DiskTotalText
    $mainboard = Get-MainboardName
    $uptime = Get-UptimeText
    $windowsEdition = Get-WindowsEdition
    $windowsRelease = Get-WindowsVersionLabel
    $windowsVersionText = if ($windowsEdition -and $windowsRelease) { "$windowsEdition | $windowsRelease" } elseif ($windowsEdition) { $windowsEdition } elseif ($windowsRelease) { $windowsRelease } else { [System.Environment]::OSVersion.VersionString }

    $payload = [ordered]@{
        deviceName = $device
        computerName = $device
        username = $env:USERNAME
        department = ""
        location = ""
        operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        siteName = if ($site) { [string]$site.site } else { "" }
        region = if ($site) { [string]$site.region } else { "" }
        isInternal = $isInternal
        appVersion = "1.0.0"
        macAddress = $macAddress
        networkType = $network.Type
        wifiSignalDbm = $network.Signal
        lanLinkSpeed = $network.LinkSpeed
        cpuPercent = $cpu
        ramPercent = $ram
        diskPercent = $disk
        networkLatencyMs = 0
        adJoined = $true
        trellixInstalled = $true
        desktopCentralInstalled = $true
        loggedInUser = $env:USERNAME
        lanIp = $lanIp
        publicIp = ""
        domain = ""
        windowsVersion = $windowsVersionText
        serialNumber = ""
        cpuModel = $cpuModel
        ramTotal = $ramTotal
        diskTotal = $diskTotal
        mainboard = $mainboard
        uptime = $uptime
    }

    $json = $payload | ConvertTo-Json -Depth 10
    $uri = "$ApiUrl/api/heartbeat"
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-RestMethod -Method Post -Uri $uri -ContentType "application/json" -Body $json -TimeoutSec 15 | Out-Null
            Write-Host "Heartbeat sent to $uri for device $device"
            return
        }
        catch {
            $message = "$(Get-Date -Format o) Heartbeat attempt $attempt failed for $device`: $($_.Exception.Message)"
            Add-Content -Path (Join-Path $PSScriptRoot 'heartbeat-error.log') -Value $message
            if ($attempt -lt 3) {
                Start-Sleep -Seconds 5
            }
        }
    }
}

if ($Once) {
    Send-Heartbeat
    exit 0
}

while ($true) {
    Send-Heartbeat
    Start-Sleep -Seconds $IntervalSeconds
}
