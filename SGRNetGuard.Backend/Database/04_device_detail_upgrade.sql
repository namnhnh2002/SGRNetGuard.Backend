/*
    SGR NetGuard - DB upgrade cho trang chi tiết máy
    Chạy sau 01_schema.sql, 02_seed_sites.sql, 03_dashboard_upgrade.sql
*/

USE [SGRNetGuard];
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'LoggedInUser') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD LoggedInUser NVARCHAR(256) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'LanIp') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD LanIp NVARCHAR(64) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'PublicIp') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD PublicIp NVARCHAR(64) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'MacAddress') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD MacAddress NVARCHAR(64) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'Domain') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD Domain NVARCHAR(256) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'WindowsVersion') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD WindowsVersion NVARCHAR(512) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'SerialNumber') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD SerialNumber NVARCHAR(256) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'CpuModel') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD CpuModel NVARCHAR(512) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'RamTotal') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD RamTotal NVARCHAR(128) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'DiskTotal') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD DiskTotal NVARCHAR(128) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'Mainboard') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD Mainboard NVARCHAR(512) NULL;
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'Uptime') IS NULL
    ALTER TABLE dbo.DeviceHeartbeats ADD Uptime NVARCHAR(128) NULL;
GO
