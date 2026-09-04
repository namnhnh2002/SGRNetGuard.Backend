$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$apiProject = Join-Path $root "Api\SGRNetGuard.Api.csproj"
$outputFolder = Join-Path $root "publish\SGRNetGuard.Api"

Write-Host "Publishing API to: $outputFolder"

if (-not (Test-Path $apiProject)) {
    throw "Không tìm thấy file project: $apiProject"
}

New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null

dotnet publish $apiProject -c Release -o $outputFolder --nologo

Write-Host ""
Write-Host "Publish xong. Thư mục cài đặt: $outputFolder"
Write-Host "Bước tiếp theo:"
Write-Host "  1. Cập nhật connection string trong appsettings.json nếu cần"
Write-Host "  2. Chạy: $outputFolder\SGRNetGuard.Api.exe"
Write-Host "  3. Mở trình duyệt: http://localhost:5080/"
