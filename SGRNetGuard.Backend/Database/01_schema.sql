/*
    SGR NetGuard - Database Schema
    Chạy script này 1 lần trên SQL Server để khởi tạo database.
    Thay thế cho việc lưu Site/DNS/SSID tĩnh trong config.json --> giờ lưu tập trung ở đây.
*/

CREATE DATABASE SGRNetGuard;
GO

USE SGRNetGuard;
GO

-- ============================================================
-- 1. Bảng Sites: thay thế phần "Sites" trong config.json cũ.
--    Đây là nguồn dữ liệu DUY NHẤT cho toàn bộ site + IT phụ trách.
--    Khi IT nghỉ việc / đổi người, chỉ cần UPDATE 1 dòng ở đây,
--    toàn bộ máy user sẽ tự đồng bộ trong lần fetch config tiếp theo.
-- ============================================================
CREATE TABLE dbo.Sites (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    Region        NVARCHAR(10)   NOT NULL,      -- VMB / VMT / VMN
    SiteName      NVARCHAR(200)  NOT NULL,
    Subnet        NVARCHAR(50)   NOT NULL,       -- CIDR, vd: 10.29.0.0/16
    ItAccount     NVARCHAR(100)  NOT NULL,       -- vd: HUNGVD
    TeamsAccount  NVARCHAR(200)  NOT NULL,       -- email đầy đủ dùng để mở chat Teams
    IsActive      BIT            NOT NULL DEFAULT 1,
    UpdatedAtUtc  DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedBy     NVARCHAR(100)  NULL
);
GO

CREATE INDEX IX_Sites_Region ON dbo.Sites(Region);
GO

-- ============================================================
-- 2. Bảng cấu hình chung: DNS servers nội bộ theo từng vùng, SSID hợp lệ.
--    (Khắc phục vấn đề "1 danh sách DNS dùng chung cho cả nước" đã phát hiện trước đó)
-- ============================================================
CREATE TABLE dbo.RegionDnsServers (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    Region    NVARCHAR(10)  NOT NULL,   -- VMB / VMT / VMN
    DnsServer NVARCHAR(50)  NOT NULL
);
GO

CREATE TABLE dbo.AppSettings (
    [Key]   NVARCHAR(100) PRIMARY KEY,
    [Value] NVARCHAR(500) NOT NULL
);
GO
INSERT INTO dbo.AppSettings ([Key], [Value]) VALUES ('ValidSsid', 'SGR-OFFICE');
GO

-- ============================================================
-- 3. Bảng ghi nhận sự kiện cảnh báo hiệu năng (CPU/RAM/Disk cao liên tục >5 phút)
--    Mỗi lần Popup "Cảnh báo hiệu năng hệ thống" hiện lên trên máy user -> gửi 1 record về đây.
-- ============================================================
CREATE TABLE dbo.PerformanceWarnings (
    Id           BIGINT IDENTITY(1,1) PRIMARY KEY,
    DeviceName   NVARCHAR(100) NOT NULL,     -- vd: BNC-HIENNT05-PC
    SiteName     NVARCHAR(200) NULL,          -- site đang đứng lúc cảnh báo (có thể null nếu mạng ngoài)
    Region       NVARCHAR(10)  NULL,
    MetricType   NVARCHAR(20)  NOT NULL,      -- CPU / RAM / Disk
    MetricValue  DECIMAL(5,2)  NOT NULL,      -- % tại thời điểm cảnh báo
    WarnedAtUtc  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE INDEX IX_PerfWarnings_Device_Time ON dbo.PerformanceWarnings(DeviceName, WarnedAtUtc);
GO

-- ============================================================
-- 4. (Tuỳ chọn) Bảng heartbeat - biết máy nào đang online, đứng site nào,
--    hữu ích để biết máy có thực sự cài app hay không, phục vụ báo cáo triển khai.
-- ============================================================
CREATE TABLE dbo.DeviceHeartbeats (
    DeviceName    NVARCHAR(100) PRIMARY KEY,
    LastSiteName  NVARCHAR(200) NULL,
    LastRegion    NVARCHAR(10)  NULL,
    LastInternalSeenUtc DATETIME2 NULL,
    IsInternal    BIT           NOT NULL DEFAULT 0,
    AppVersion    NVARCHAR(20)  NULL,
    LastSeenUtc   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- ============================================================
-- 5. View tổng hợp báo cáo hàng tuần: số lần cảnh báo hiệu năng theo từng máy, 7 ngày gần nhất
-- ============================================================
CREATE OR ALTER VIEW dbo.vw_WeeklyPerformanceReport AS
SELECT
    DeviceName,
    SiteName,
    Region,
    SUM(CASE WHEN MetricType = 'CPU'  THEN 1 ELSE 0 END) AS CpuWarningCount,
    SUM(CASE WHEN MetricType = 'RAM'  THEN 1 ELSE 0 END) AS RamWarningCount,
    SUM(CASE WHEN MetricType = 'Disk' THEN 1 ELSE 0 END) AS DiskWarningCount,
    COUNT(*) AS TotalWarningCount,
    MAX(WarnedAtUtc) AS LastWarningUtc
FROM dbo.PerformanceWarnings
WHERE WarnedAtUtc >= DATEADD(DAY, -7, SYSUTCDATETIME())
GROUP BY DeviceName, SiteName, Region;
GO

-- ============================================================
-- Dữ liệu mẫu: import Site từ config.json cũ (chạy 1 lần, có thể generate bằng script Python đính kèm)
-- Xem file seed_sites_from_config.sql
-- ============================================================
