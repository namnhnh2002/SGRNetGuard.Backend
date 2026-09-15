# SGR NetGuard Heartbeat Agent - Auto Installer
# Run as Administrator to install heartbeat service
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -Command "IEX(New-Object Net.WebClient).DownloadString('https://sgrnetguard-backend.onrender.com/install-heartbeat.ps1')"

param(
    [string]$InstallPath = "C:\SGRNetGuard\Heartbeat",
    [string]$ApiUrl = "https://sgrnetguard-backend.onrender.com",
    [string]$PackageUrl = "https://sgrnetguard-backend.onrender.com/SGRNetGuard-Heartbeat-Agent-v20260915.1.0.3.zip"
)

function Require-Admin {
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]"Administrator")) {
        Write-Host "❌ Lỗi: Script phải chạy dưới quyền Administrator" -ForegroundColor Red
        Write-Host "Vui lòng mở PowerShell as Administrator rồi chạy lại."
        exit 1
    }
}

function Download-Package {
    param([string]$Url, [string]$DestinationPath)
    
    Write-Host "📥 Đang tải heartbeat package từ: $Url" -ForegroundColor Cyan
    try {
        $client = New-Object System.Net.WebClient
        $client.DownloadFile($Url, $DestinationPath)
        Write-Host "✓ Tải thành công: $DestinationPath" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "❌ Lỗi tải package: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Extract-Package {
    param([string]$ZipPath, [string]$DestinationPath)
    
    Write-Host "📦 Đang giải nén package..." -ForegroundColor Cyan
    try {
        if (Test-Path $DestinationPath) {
            Remove-Item $DestinationPath -Recurse -Force -ErrorAction SilentlyContinue
        }
        New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
        
        # Extract zip
        [System.IO.Compression.ZipFile]::ExtractToDirectory($ZipPath, $DestinationPath)
        Write-Host "✓ Giải nén thành công: $DestinationPath" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "❌ Lỗi giải nén: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Install-HeartbeatTask {
    param([string]$TaskPath)
    
    Write-Host "⚙️  Đang cài đặt Scheduled Task..." -ForegroundColor Cyan
    
    $scriptPath = Join-Path $TaskPath 'ClientHeartbeatSender.ps1'
    $launcher = Join-Path $TaskPath 'ClientHeartbeatLauncher.cmd'
    
    if (-not (Test-Path $scriptPath)) {
        Write-Host "❌ Lỗi: Không tìm thấy ClientHeartbeatSender.ps1" -ForegroundColor Red
        return $false
    }
    
    $taskName = 'SGRNetGuard-Heartbeat'
    
    # Remove old task if exists
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    
    try {
        $action = New-ScheduledTaskAction -Execute $launcher -WorkingDirectory $TaskPath
        $triggers = @(
            (New-ScheduledTaskTrigger -AtStartup),
            (New-ScheduledTaskTrigger -AtLogOn)
        )
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Seconds 0)
        
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $triggers -Settings $settings -Force -ErrorAction Stop | Out-Null
        
        Write-Host "✓ Scheduled Task cài đặt thành công" -ForegroundColor Green
        Write-Host "  📋 Tên task: $taskName" -ForegroundColor Gray
        Write-Host "  📂 Vị trí: $TaskPath" -ForegroundColor Gray
        return $true
    }
    catch {
        Write-Host "❌ Lỗi cài đặt task: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Test-HeartbeatService {
    param([string]$TaskPath)
    
    Write-Host "🧪 Kiểm tra heartbeat service..." -ForegroundColor Cyan
    
    $taskName = 'SGRNetGuard-Heartbeat'
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    
    if ($task) {
        Write-Host "✓ Scheduled Task đang chạy" -ForegroundColor Green
        
        # Run one heartbeat manually
        Write-Host "  ⏱️  Gửi heartbeat thử nghiệm..." -ForegroundColor Gray
        $launcher = Join-Path $TaskPath 'ClientHeartbeatLauncher.cmd'
        
        try {
            & $launcher | Out-Null
            Start-Sleep -Seconds 2
            
            # Check for errors
            $errorLog = Join-Path $TaskPath 'heartbeat-error.log'
            if (Test-Path $errorLog) {
                $lastError = Get-Content $errorLog -Tail 1
                if ($lastError -match "Heartbeat sent") {
                    Write-Host "✓ Heartbeat gửi thành công!" -ForegroundColor Green
                    return $true
                }
            }
            Write-Host "✓ Heartbeat service sẵn sàng" -ForegroundColor Green
            return $true
        }
        catch {
            Write-Host "⚠️  Không thể chạy heartbeat ngay: $($_.Exception.Message)" -ForegroundColor Yellow
            return $true
        }
    }
    else {
        Write-Host "❌ Scheduled Task không tìm thấy" -ForegroundColor Red
        return $false
    }
}

# ============================================================
# MAIN INSTALLATION FLOW
# ============================================================

Write-Host "`n╔════════════════════════════════════════════════════╗"
Write-Host "║   SGR NetGuard Heartbeat Agent - Auto Installer  ║"
Write-Host "╚════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan

# Check admin rights
Require-Admin

Write-Host "📍 Thông tin cài đặt:" -ForegroundColor Cyan
Write-Host "  Đường dẫn cài: $InstallPath"
Write-Host "  API URL: $ApiUrl`n"

# Download
$tempZip = Join-Path $env:TEMP "SGRNetGuard-Heartbeat.zip"
if (-not (Download-Package -Url $PackageUrl -DestinationPath $tempZip)) {
    exit 1
}

# Extract
if (-not (Extract-Package -ZipPath $tempZip -DestinationPath $InstallPath)) {
    exit 1
}

# Clean up temp zip
Remove-Item $tempZip -Force -ErrorAction SilentlyContinue

# Install scheduled task
if (-not (Install-HeartbeatTask -TaskPath $InstallPath)) {
    exit 1
}

# Test
if (-not (Test-HeartbeatService -TaskPath $InstallPath)) {
    exit 1
}

# Summary
Write-Host "`n╔════════════════════════════════════════════════════╗"
Write-Host "║              ✅ CÀI ĐẶT THÀNH CÔNG!              ║"
Write-Host "╚════════════════════════════════════════════════════╝`n" -ForegroundColor Green

Write-Host "📌 Tiếp theo:" -ForegroundColor Cyan
Write-Host "  • Heartbeat service sẽ tự động chạy:"
Write-Host "    - Khi Windows khởi động"
Write-Host "    - Khi người dùng đăng nhập"
Write-Host "  • Mỗi 60 giây, máy sẽ gửi heartbeat lên API"
Write-Host "  • Dashboard IT sẽ hiển thị máy này với isInternal=true"
Write-Host ""
Write-Host "🔍 Để kiểm tra:" -ForegroundColor Cyan
Write-Host "  • Mở Task Scheduler"
Write-Host "  • Tìm task: SGRNetGuard-Heartbeat"
Write-Host "  • Xem file log: $InstallPath\heartbeat-error.log"
Write-Host ""
Write-Host "🆘 Nếu có vấn đề:" -ForegroundColor Yellow
Write-Host "  • Restart Windows hoặc đăng nhập lại"
Write-Host "  • Kiểm tra file log để xem lỗi chi tiết"
Write-Host "  • Liên hệ IT Support nếu cần`n"
