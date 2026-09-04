/*
    Preserve the last region identified while a device was on the internal network.
    Run after 01_schema.sql and the existing device upgrades.
*/

USE [SGRNetGuard];
GO

IF COL_LENGTH('dbo.DeviceHeartbeats', 'LastInternalSeenUtc') IS NULL
BEGIN
    ALTER TABLE dbo.DeviceHeartbeats ADD LastInternalSeenUtc DATETIME2 NULL;
END
GO