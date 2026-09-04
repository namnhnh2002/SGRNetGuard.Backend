/* Bổ sung thông tin kết nối hiện tại của adapter active cho Dashboard. */
USE [SGRNetGuard];
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'NetworkType') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD NetworkType NVARCHAR(20) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'WifiSignalDbm') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD WifiSignalDbm INT NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'LanLinkSpeed') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD LanLinkSpeed NVARCHAR(64) NULL;
GO
