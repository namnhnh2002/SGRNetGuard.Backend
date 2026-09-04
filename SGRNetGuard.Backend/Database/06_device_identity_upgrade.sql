/*
    SGR NetGuard - Device identity + history + software inventory + compliance + alerts
    Chạy sau 01_schema.sql, 02_seed_sites.sql, 03_dashboard_upgrade.sql, 04_device_detail_upgrade.sql, 05_device_detail_timestamp_upgrade.sql
*/

USE [SGRNetGuard];
GO

IF OBJECT_ID('dbo.Devices', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Devices (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        DeviceId UNIQUEIDENTIFIER NOT NULL UNIQUE,
        MACAddress NVARCHAR(64) NULL,
        ComputerName NVARCHAR(256) NULL,
        CurrentUser NVARCHAR(256) NULL,
        CurrentDepartment NVARCHAR(256) NULL,
        CurrentLocation NVARCHAR(256) NULL,
        OperatingSystem NVARCHAR(512) NULL,
        FirstSeen DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        LastSeen DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        Status NVARCHAR(50) NOT NULL DEFAULT 'Active',
        NetworkWarningDisabled BIT NOT NULL DEFAULT 0,
        NetworkWarningDisabledAt DATETIME2 NULL
    );
END
GO

IF OBJECT_ID('dbo.DeviceHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DeviceHistory (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        DeviceId UNIQUEIDENTIFIER NOT NULL,
        MACAddress NVARCHAR(64) NULL,
        ComputerName NVARCHAR(256) NULL,
        Username NVARCHAR(256) NULL,
        Department NVARCHAR(256) NULL,
        Location NVARCHAR(256) NULL,
        Timestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        ChangeType NVARCHAR(100) NOT NULL DEFAULT 'DEVICE_INFO_CHANGED'
    );
END
GO

IF OBJECT_ID('dbo.NetworkStatus', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NetworkStatus (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        DeviceId UNIQUEIDENTIFIER NOT NULL,
        ConnectionType NVARCHAR(50) NULL,
        AdapterName NVARCHAR(256) NULL,
        IPAddress NVARCHAR(64) NULL,
        MACAddress NVARCHAR(64) NULL,
        SSID NVARCHAR(256) NULL,
        SignalStrengthDbm DECIMAL(6,2) NULL,
        LinkSpeedMbps DECIMAL(10,2) NULL,
        DownloadMbps DECIMAL(10,2) NULL,
        UploadMbps DECIMAL(10,2) NULL,
        Timestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID('dbo.PerformanceLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformanceLogs (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        DeviceId UNIQUEIDENTIFIER NOT NULL,
        CPUUsage DECIMAL(6,2) NULL,
        RAMUsage DECIMAL(6,2) NULL,
        DiskUsage DECIMAL(6,2) NULL,
        DiskRead DECIMAL(10,2) NULL,
        DiskWrite DECIMAL(10,2) NULL,
        DiskIO DECIMAL(10,2) NULL,
        TopProcess NVARCHAR(512) NULL,
        Timestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID('dbo.ComplianceStatus', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComplianceStatus (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        DeviceId UNIQUEIDENTIFIER NOT NULL,
        Antivirus NVARCHAR(50) NULL,
        Firewall NVARCHAR(50) NULL,
        WindowsUpdate NVARCHAR(50) NULL,
        BitLocker NVARCHAR(50) NULL,
        PasswordPolicy NVARCHAR(50) NULL,
        EndpointProtection NVARCHAR(50) NULL,
        OverallStatus NVARCHAR(50) NOT NULL DEFAULT 'Unknown',
        Timestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID('dbo.SoftwareInventory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SoftwareInventory (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        DeviceId UNIQUEIDENTIFIER NOT NULL,
        SoftwareName NVARCHAR(256) NOT NULL,
        Version NVARCHAR(128) NULL,
        Publisher NVARCHAR(256) NULL,
        InstallDate DATETIME2 NULL,
        FirstDetected DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        LastDetected DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID('dbo.Alerts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Alerts (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        DeviceId UNIQUEIDENTIFIER NULL,
        AlertType NVARCHAR(50) NOT NULL,
        Severity NVARCHAR(20) NOT NULL DEFAULT 'Warning',
        Title NVARCHAR(256) NOT NULL,
        Message NVARCHAR(1000) NULL,
        IsAcknowledged BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        ResolvedAt DATETIME2 NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DeviceHistory_DeviceId_Time' AND object_id = OBJECT_ID('dbo.DeviceHistory'))
    CREATE INDEX IX_DeviceHistory_DeviceId_Time ON dbo.DeviceHistory(DeviceId, Timestamp);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NetworkStatus_DeviceId_Time' AND object_id = OBJECT_ID('dbo.NetworkStatus'))
    CREATE INDEX IX_NetworkStatus_DeviceId_Time ON dbo.NetworkStatus(DeviceId, Timestamp);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PerformanceLogs_DeviceId_Time' AND object_id = OBJECT_ID('dbo.PerformanceLogs'))
    CREATE INDEX IX_PerformanceLogs_DeviceId_Time ON dbo.PerformanceLogs(DeviceId, Timestamp);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ComplianceStatus_DeviceId_Time' AND object_id = OBJECT_ID('dbo.ComplianceStatus'))
    CREATE INDEX IX_ComplianceStatus_DeviceId_Time ON dbo.ComplianceStatus(DeviceId, Timestamp);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SoftwareInventory_DeviceId_Name' AND object_id = OBJECT_ID('dbo.SoftwareInventory'))
    CREATE INDEX IX_SoftwareInventory_DeviceId_Name ON dbo.SoftwareInventory(DeviceId, SoftwareName);
GO
