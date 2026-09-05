-- Reset only device and telemetry data.
-- Preserves Sites, RegionDnsServers, AppSettings, and dashboard configuration.
-- Run against the PostgreSQL database used by Render.

BEGIN;

TRUNCATE TABLE
    public.PerformanceWarnings,
    public.DeviceHistory,
    public.NetworkStatus,
    public.PerformanceLogs,
    public.ComplianceStatus,
    public.SoftwareInventory,
    public.Alerts,
    public.DeviceHeartbeats,
    public.Devices
RESTART IDENTITY;

COMMIT;

-- Verify the reset. All counts below should be 0.
SELECT 'DeviceHeartbeats' AS table_name, COUNT(*) AS row_count FROM public.DeviceHeartbeats
UNION ALL SELECT 'Devices', COUNT(*) FROM public.Devices
UNION ALL SELECT 'PerformanceWarnings', COUNT(*) FROM public.PerformanceWarnings
UNION ALL SELECT 'NetworkStatus', COUNT(*) FROM public.NetworkStatus
UNION ALL SELECT 'PerformanceLogs', COUNT(*) FROM public.PerformanceLogs
UNION ALL SELECT 'ComplianceStatus', COUNT(*) FROM public.ComplianceStatus
UNION ALL SELECT 'SoftwareInventory', COUNT(*) FROM public.SoftwareInventory
UNION ALL SELECT 'Alerts', COUNT(*) FROM public.Alerts;
