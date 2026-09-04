/*
    Mở rộng schema cho Web Dashboard IT: thêm chỉ số hiệu năng live (CPU/RAM/Disk),
    trạng thái tuân thủ ANBM (AD Join / Trellix / Desktop Central) vào DeviceHeartbeats
    - đây là dữ liệu Dashboard cần hiển thị real-time cho từng máy.
    Chạy sau khi đã chạy 01_schema.sql và 02_seed_sites.sql.
*/
USE SGRNetGuard;
GO

ALTER TABLE dbo.DeviceHeartbeats ADD
    CpuPercent      DECIMAL(5,2)  NULL,
    RamPercent      DECIMAL(5,2)  NULL,
    DiskPercent     DECIMAL(5,2)  NULL,
    NetworkLatencyMs INT          NULL,
    AdJoined                 BIT NULL,
    TrellixInstalled          BIT NULL,
    DesktopCentralInstalled   BIT NULL;
GO

-- View tiện dùng cho Dashboard: danh sách máy + trạng thái mới nhất + có đang "online" hay không
-- (coi là online nếu LastSeenUtc trong vòng 3 phút gần nhất, tương ứng heartbeat mỗi ~1-2 phút từ client)
CREATE OR ALTER VIEW dbo.vw_DeviceDashboard AS
SELECT
    h.DeviceName,
    h.LastSiteName,
    h.LastRegion,
    h.IsInternal,
    h.CpuPercent,
    h.RamPercent,
    h.DiskPercent,
    h.NetworkLatencyMs,
    h.AdJoined,
    h.TrellixInstalled,
    h.DesktopCentralInstalled,
    h.AppVersion,
    h.LastSeenUtc,
    CASE WHEN h.LastSeenUtc >= DATEADD(MINUTE, -5, SYSUTCDATETIME()) THEN 1 ELSE 0 END AS IsOnline,
    (SELECT COUNT(*) FROM dbo.PerformanceWarnings w
     WHERE w.DeviceName = h.DeviceName AND w.WarnedAtUtc >= CAST(SYSUTCDATETIME() AS DATE)) AS WarningsToday
FROM dbo.DeviceHeartbeats h;
GO
