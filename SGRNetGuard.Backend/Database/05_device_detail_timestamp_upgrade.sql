/*
    SGR NetGuard - thêm mốc thời gian riêng cho thông tin máy tĩnh
    Chạy sau 04_device_detail_upgrade.sql
*/

USE [SGRNetGuard];
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'DetailUpdatedUtc') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD DetailUpdatedUtc DATETIME2 NULL;
GO

UPDATE dbo.DeviceHeartbeats
SET DetailUpdatedUtc = LastSeenUtc
WHERE DetailUpdatedUtc IS NULL
  AND (
      NULLIF(LoggedInUser, '') IS NOT NULL
      OR NULLIF(LanIp, '') IS NOT NULL
      OR NULLIF(PublicIp, '') IS NOT NULL
      OR NULLIF(MacAddress, '') IS NOT NULL
      OR NULLIF(Domain, '') IS NOT NULL
      OR NULLIF(WindowsVersion, '') IS NOT NULL
      OR NULLIF(SerialNumber, '') IS NOT NULL
      OR NULLIF(CpuModel, '') IS NOT NULL
      OR NULLIF(RamTotal, '') IS NOT NULL
      OR NULLIF(DiskTotal, '') IS NOT NULL
      OR NULLIF(Mainboard, '') IS NOT NULL
      OR NULLIF(Uptime, '') IS NOT NULL
  );
GO
