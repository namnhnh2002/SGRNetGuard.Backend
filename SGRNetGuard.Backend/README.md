# SGR NetGuard Backend — Config tập trung + Báo cáo hiệu năng tuần

Module này giải quyết 2 việc bạn yêu cầu:
1. **Hướng B**: Site/DNS/SSID quản lý tập trung ở database, sửa 1 chỗ → toàn bộ máy user tự đồng bộ (không cần push file thủ công nữa).
2. **Báo cáo tuần**: đếm số lần mỗi máy bị cảnh báo hiệu năng (CPU/RAM/Disk), xuất Excel, gửi email tự động mỗi tuần.

## Kiến trúc tổng quan

```
[App SGR NetGuard trên máy user]
        │
        │ (1) GET /api/config          — lấy Site/DNS/SSID mới nhất, cache local, gọi lại mỗi 6h
        │ (2) POST /api/telemetry/warning — gửi mỗi khi hiện Popup cảnh báo hiệu năng
        ▼
[SGRNetGuard.Api — ASP.NET Core Web API, host trên IIS nội bộ]
        │
        ▼
[PostgreSQL: database sgrnetguard]
        ▲
        │ đọc dữ liệu 7 ngày gần nhất
        │
[SGRNetGuard.WeeklyReportJob — console app, Windows Task Scheduler chạy 1 lần/tuần]
        │
        ▼
   Excel + Email gửi cho IT Manager
```

## 1. Setup Database

Trên PostgreSQL, chạy bằng `psql`:
```
Database/01_schema.sql              -- tạo database + toàn bộ bảng/view
Database/02_seed_sites.sql          -- import 89 site đã có sẵn từ config.json cũ
Database/03_dashboard_upgrade.sql   -- thêm cột CPU/RAM/Disk/ANBM cho Dashboard IT
Database/04_device_detail_upgrade.sql -- thêm cột chi tiết máy cho trang Device Detail
Database/06_device_identity_upgrade.sql -- thêm Devices/DeviceHistory/NetworkStatus/PerformanceLogs/ComplianceStatus/SoftwareInventory/Alerts
Database/07_last_known_region_upgrade.sql -- lưu vùng nội bộ gần nhất khi máy chuyển sang mạng ngoài
```

**Việc cần làm thêm ngay sau đó:**
- Sửa email Teams thật cho từng site trong bảng `dbo.Sites` (cột `TeamsAccount`) — hiện đang là giá trị tạm `<it>@sungroup.com.vn`.
- Thêm DNS server thật của **VMT và VMN** vào bảng `dbo.RegionDnsServers` (hiện mới chỉ có DNS của VMB).

**Từ giờ về sau, khi IT nghỉ việc / đổi người phụ trách site:**
```sql
UPDATE dbo.Sites
SET ItAccount = 'TENIT_MOI', Teams = 'tenit_moi@sungroup.com.vn', UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = 'ten_admin'
WHERE SiteName = N'Tên site cần đổi';
```
Chỉ 1 câu lệnh này — toàn bộ máy user sẽ tự nhận thông tin mới trong lần đồng bộ tiếp theo (tối đa 6 tiếng, hoặc ngay lập tức nếu app đang khởi động lại).

## 2. Deploy Web API

```powershell
cd Api
dotnet restore
dotnet publish -c Release -o C:\inetpub\SGRNetGuardApi
```

- Sửa `ConnectionStrings__SGRNetGuard` bằng chuỗi Npgsql (`Host=...;Port=5432;Database=...;Username=...;Password=...`) trước khi publish. Trên Render có thể dùng trực tiếp `DATABASE_URL` hoặc biến `ConnectionStrings__SGRNetGuard`.
- Tạo Application Pool mới trên IIS, .NET CLR Version = "No Managed Code" (vì đây là ASP.NET Core, tự host Kestrel qua module ANCM).
- Cài **ASP.NET Core Hosting Bundle** trên server IIS nếu chưa có: https://dotnet.microsoft.com/download/dotnet/8.0
- Trỏ site IIS vào `C:\inetpub\SGRNetGuardApi`, port tùy bạn (mặc định code đang dùng `5080`, có thể đổi qua IIS binding).
- Test: mở trình duyệt `http://<server-ip>:5080/api/config` → phải trả JSON danh sách 89 site.

## 3. Sửa code app hiện tại (client) để dùng config tập trung

Xem chi tiết trong `ClientIntegration/CLIENT_CODE_TO_INSERT.cs` — đây **không phải project chạy độc lập**, mà là các đoạn code cần **copy vào đúng vị trí trong project app hiện tại của bạn**:

1. Thêm file `Services/RemoteConfigClient.cs` — gọi `GET /api/config`, tự cache lại local để vẫn chạy được khi mất mạng.
2. Sửa `NetworkDetectionEngine.cs` — so khớp DNS theo **đúng vùng** của site (VMB/VMT/VMN) thay vì 1 danh sách DNS chung như trước.
3. Sửa `App.xaml.cs` — gọi `RemoteConfigClient.LoadAsync()` lúc khởi động + Timer tải lại mỗi 6 tiếng.
4. Thêm file `Services/TelemetryClient.cs` — gọi `POST /api/telemetry/warning` ngay tại đoạn code hiện tại của bạn đang hiển thị Popup "Cảnh báo hiệu năng hệ thống" (ảnh đỏ CPU/RAM/Disk bạn gửi).

**Lưu ý quan trọng:** đổi `ApiBaseUrl` trong cả 2 file (`RemoteConfigClient.cs`, `TelemetryClient.cs`) thành địa chỉ IIS thật sau khi deploy xong bước 2.

## 3.5. Web Dashboard cho IT (realtime + danh sách máy)

Dashboard nằm sẵn trong project API (`Api/wwwroot/`) — **không cần deploy riêng**, chạy chung 1 IIS site với API ở bước 2. Sau khi publish xong bước 2, IT chỉ cần mở trình duyệt vào:

```
http://<server-ip>:5080/
```

Tính năng:
- **Bảng danh sách toàn bộ máy** — tên máy, site đang đứng, vùng, trạng thái mạng nội bộ/ngoài, CPU/RAM/Disk hiện tại, tình trạng tuân thủ ANBM (AD Join / Trellix / Desktop Central), số lần cảnh báo trong ngày, thời gian cập nhật gần nhất.
- **Tìm kiếm + lọc** theo tên máy/site, theo vùng (VMB/VMT/VMN), theo trạng thái (online/offline/chưa tuân thủ ANBM).
- **Cảnh báo realtime** — dùng SignalR: ngay khi có máy nào bị cảnh báo hiệu năng, toast thông báo hiện góc phải màn hình cho MỌI trình duyệt IT đang mở Dashboard, không cần bấm refresh.
- Máy nào chưa tuân thủ ANBM (thiếu AD Join / Trellix / Desktop Central) sẽ được **tô đỏ nhạt cả dòng** để dễ nhận diện.
- **Xuất Excel theo danh sách máy** — tích chọn từng máy trên bảng rồi bấm **Xuất báo cáo** để tải báo cáo các máy đã chọn.
- **Click vào tên máy** để mở trang chi tiết: user đăng nhập, IP LAN/Public, MAC, Domain, Windows, Serial, CPU/RAM/Disk, Mainboard, Uptime, Agent Version.

**Điều kiện để Dashboard có dữ liệu:** client (app trên máy user) phải gọi `POST /api/heartbeat` định kỳ (khuyến nghị mỗi 60 giây) kèm CPU/RAM/Disk/ANBM hiện tại — xem mục 5 trong `ClientIntegration/CLIENT_CODE_TO_INSERT.cs`. Nếu chỉ có `POST /api/telemetry/warning` (lúc có cảnh báo) mà chưa có heartbeat định kỳ, Dashboard vẫn nhận được toast realtime nhưng bảng danh sách máy sẽ trống hoặc dữ liệu cũ.



```powershell
cd WeeklyReportJob
dotnet restore
dotnet publish -c Release -o C:\SGRNetGuard\WeeklyReportJob
```

Sửa `appsettings.json` trong thư mục publish: connection string SQL Server + thông tin SMTP + danh sách email nhận báo cáo (`Email:ToAddresses`).

**Lên lịch chạy hàng tuần** bằng Windows Task Scheduler (trên server, không phải máy user):
```powershell
$action = New-ScheduledTaskAction -Execute "C:\SGRNetGuard\WeeklyReportJob\SGRNetGuard.WeeklyReportJob.exe"
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday -At 8am
Register-ScheduledTask -TaskName "SGRNetGuard-WeeklyReport" -Action $action -Trigger $trigger -RunLevel Highest
```
→ Mỗi thứ Hai 8h sáng, job tự chạy: query dữ liệu 7 ngày qua, xuất Excel, gửi email cho IT Manager.

Test thử ngay (không cần đợi lịch):
```powershell
C:\SGRNetGuard\WeeklyReportJob\SGRNetGuard.WeeklyReportJob.exe
```

## 5. Nội dung báo cáo Excel

Mỗi dòng = 1 máy tính, gồm:
- Tên máy, Site đang đứng, Vùng
- Số lần cảnh báo CPU / RAM / Disk riêng biệt + tổng
- Thời điểm cảnh báo gần nhất
- Máy có ≥ 20 lần cảnh báo trong tuần sẽ **tô đỏ nhạt** để IT dễ nhận diện máy cần kiểm tra ưu tiên.

## 6. Những điểm cần bạn xác nhận / chuẩn bị

- Thông tin kết nối PostgreSQL thật (host, database, user/password).
- Địa chỉ/port sẽ host Web API trên IIS nội bộ (để cập nhật `ApiBaseUrl` phía client).
- Thông tin SMTP nội bộ để gửi email (host, port, tài khoản) — nếu công ty dùng Microsoft 365/Exchange Online thì cần app password hoặc SMTP relay riêng, không dùng trực tiếp mật khẩu tài khoản O365 thường.
- Danh sách email/nhóm IT sẽ nhận báo cáo tuần.
- DNS server thật của vùng VMT và VMN (vẫn đang thiếu từ trước).
