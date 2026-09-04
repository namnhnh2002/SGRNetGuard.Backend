using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using SGRNetGuard.Api.Models;

namespace SGRNetGuard.Api.Data;

public class SqlDataAccess
{
    private readonly string _connectionString;

    public SqlDataAccess(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("SGRNetGuard")
            ?? throw new InvalidOperationException("Thiếu ConnectionStrings:SGRNetGuard trong appsettings.json");
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    private static Guid ResolveDeviceId(string? deviceId, string? deviceName, string? macAddress)
    {
        if (Guid.TryParse(deviceId, out var parsed)) return parsed;
        if (!string.IsNullOrWhiteSpace(macAddress)) return CreateStableGuid($"mac:{macAddress}");
        if (!string.IsNullOrWhiteSpace(deviceName)) return CreateStableGuid($"name:{deviceName}");
        return Guid.NewGuid();
    }

    private static Guid CreateStableGuid(string seed)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(seed.ToLowerInvariant()));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }

    private static bool IsPrivateIpv4(string? ipValue)
    {
        if (string.IsNullOrWhiteSpace(ipValue)) return false;

        if (!IPAddress.TryParse(ipValue.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
               (bytes[0] == 169 && bytes[1] == 254);
    }

    private static bool ResolveIsInternal(bool isInternal, string? lanIp, string? publicIp)
    {
        return isInternal;
    }
    public async Task<RemoteConfigDto> GetActiveConfigAsync()
    {
        using var conn = CreateConnection();

        var sites = (await conn.QueryAsync<SiteDto>(
            @"SELECT Region, SiteName AS Site, Subnet, ItAccount AS IT, TeamsAccount AS Teams
              FROM dbo.Sites WHERE IsActive = 1 ORDER BY Region, SiteName")).ToList();

        var dnsRows = await conn.QueryAsync<(string Region, string DnsServer)>(
            "SELECT Region, DnsServer FROM dbo.RegionDnsServers");

        var dnsByRegion = dnsRows
            .GroupBy(r => r.Region)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DnsServer).ToList());

        var ssid = await conn.ExecuteScalarAsync<string>(
            "SELECT [Value] FROM dbo.AppSettings WHERE [Key] = 'ValidSsid'") ?? "SGR-OFFICE";

        var version = await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT MAX(UpdatedAtUtc) FROM dbo.Sites");

        return new RemoteConfigDto
        {
            ConfigVersion = (version ?? DateTime.UtcNow).ToString("O"),
            Ssid = ssid,
            DnsServersByRegion = dnsByRegion,
            Sites = sites
        };
    }

    // ---------------- Performance Warnings ----------------

    public async Task InsertWarningAsync(PerformanceWarningDto dto)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO dbo.PerformanceWarnings (DeviceName, SiteName, Region, MetricType, MetricValue)
              VALUES (@DeviceName, @SiteName, @Region, @MetricType, @MetricValue)",
            dto);

        await EnsureWarningAlertAsync(conn, dto);
    }

    private async Task EnsureWarningAlertAsync(SqlConnection conn, PerformanceWarningDto dto)
    {
        var deviceName = dto.DeviceName;
        var title = dto.MetricType switch
        {
            "CPU" => "Cảnh báo CPU",
            "RAM" => "Cảnh báo RAM",
            _ => "Cảnh báo ổ cứng"
        };

        await conn.ExecuteAsync(
            @"INSERT INTO dbo.Alerts (DeviceId, AlertType, Severity, Title, Message, IsAcknowledged, CreatedAt)
              SELECT DeviceId, @AlertType, @Severity, @Title, @Message, 0, SYSUTCDATETIME()
              FROM dbo.Devices
              WHERE ComputerName = @DeviceName",
            new
            {
                DeviceName = deviceName,
                AlertType = "Performance",
                Severity = dto.MetricValue >= 95 ? "Critical" : "Warning",
                Title = title,
                Message = $"Máy {dto.DeviceName} đang vượt ngưỡng {dto.MetricType}"
            });
    }

    // ---------------- Heartbeat + device identity ----------------

    public async Task UpsertHeartbeatAsync(HeartbeatDto dto)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        var resolvedDeviceId = ResolveDeviceId(dto.DeviceId, dto.ComputerName ?? dto.DeviceName, dto.MacAddress);
        var effectiveDeviceName = string.IsNullOrWhiteSpace(dto.ComputerName) ? dto.DeviceName : dto.ComputerName;
        var effectiveIsInternal = ResolveIsInternal(dto.IsInternal, dto.LanIp, dto.PublicIp);

        await conn.ExecuteAsync(
            @"MERGE dbo.Devices AS target
              USING (SELECT @DeviceId AS DeviceId) AS src
              ON target.DeviceId = src.DeviceId
              WHEN MATCHED THEN UPDATE SET
                  MACAddress = COALESCE(NULLIF(@MacAddress, ''), MACAddress),
                  ComputerName = COALESCE(NULLIF(@ComputerName, ''), ComputerName),
                  CurrentUser = COALESCE(NULLIF(@Username, ''), CurrentUser),
                  CurrentDepartment = COALESCE(NULLIF(@Department, ''), CurrentDepartment),
                  CurrentLocation = COALESCE(NULLIF(@Location, ''), CurrentLocation),
                  OperatingSystem = COALESCE(NULLIF(@OperatingSystem, ''), OperatingSystem),
                  LastSeen = SYSUTCDATETIME(),
                  Status = CASE WHEN @IsInternal = 1 THEN 'Active' ELSE 'External' END
              WHEN NOT MATCHED THEN
                  INSERT (DeviceId, MACAddress, ComputerName, CurrentUser, CurrentDepartment, CurrentLocation, OperatingSystem, FirstSeen, LastSeen, Status)
                  VALUES (@DeviceId, NULLIF(@MacAddress, ''), NULLIF(@ComputerName, ''), NULLIF(@Username, ''), NULLIF(@Department, ''), NULLIF(@Location, ''), NULLIF(@OperatingSystem, ''), SYSUTCDATETIME(), SYSUTCDATETIME(), CASE WHEN @IsInternal = 1 THEN 'Active' ELSE 'External' END);",
            new
            {
                DeviceId = resolvedDeviceId,
                MacAddress = dto.MacAddress,
                ComputerName = effectiveDeviceName,
                Username = dto.Username,
                Department = dto.Department,
                Location = dto.Location,
                OperatingSystem = dto.OperatingSystem,
                IsInternal = effectiveIsInternal ? 1 : 0,
                HasNetworkData = !string.IsNullOrWhiteSpace(dto.LanIp) || !string.IsNullOrWhiteSpace(dto.PublicIp) ? 1 : 0
            },
            transaction: tx);

        await conn.ExecuteAsync(
            @"MERGE dbo.DeviceHeartbeats AS target
              USING (SELECT @DeviceName AS DeviceName) AS src
              ON target.DeviceName = src.DeviceName
              WHEN MATCHED THEN UPDATE SET
                  LastSiteName = COALESCE(NULLIF(@SiteName, ''), LastSiteName),
                  LastRegion = CASE
                      WHEN @IsInternal = 1 AND NULLIF(@Region, '') IS NOT NULL THEN @Region
                      ELSE target.LastRegion
                  END,
                  LastInternalSeenUtc = CASE
                      WHEN @HasNetworkData = 1 AND @IsInternal = 1 AND NULLIF(@Region, '') IS NOT NULL THEN SYSUTCDATETIME()
                      ELSE target.LastInternalSeenUtc
                  END,
                  IsInternal = @IsInternal,
                  AppVersion = @AppVersion,
                  CpuPercent = @CpuPercent, RamPercent = @RamPercent, DiskPercent = @DiskPercent,
                  NetworkLatencyMs = @NetworkLatencyMs,
                  NetworkType = @NetworkType,
                  WifiSignalDbm = CASE WHEN @NetworkType = 'WiFi' THEN @WifiSignalDbm ELSE NULL END,
                  LanLinkSpeed = CASE WHEN @NetworkType = 'LAN' THEN NULLIF(@LanLinkSpeed, '') ELSE NULL END,
                  AdJoined = COALESCE(@AdJoined, AdJoined),
                  TrellixInstalled = COALESCE(@TrellixInstalled, TrellixInstalled),
                  DesktopCentralInstalled = COALESCE(@DesktopCentralInstalled, DesktopCentralInstalled),
                  LoggedInUser = COALESCE(NULLIF(@LoggedInUser, ''), LoggedInUser),
                  LanIp = COALESCE(NULLIF(@LanIp, ''), LanIp),
                  PublicIp = COALESCE(NULLIF(@PublicIp, ''), PublicIp),
                  MacAddress = COALESCE(NULLIF(@MacAddress, ''), MacAddress),
                  Domain = COALESCE(NULLIF(@Domain, ''), Domain),
                  WindowsVersion = COALESCE(NULLIF(@WindowsVersion, ''), WindowsVersion),
                  SerialNumber = COALESCE(NULLIF(@SerialNumber, ''), SerialNumber),
                  CpuModel = COALESCE(NULLIF(@CpuModel, ''), CpuModel),
                  RamTotal = COALESCE(NULLIF(@RamTotal, ''), RamTotal),
                  DiskTotal = COALESCE(NULLIF(@DiskTotal, ''), DiskTotal),
                  Mainboard = COALESCE(NULLIF(@Mainboard, ''), Mainboard),
                  Uptime = COALESCE(NULLIF(@Uptime, ''), Uptime),
                  DetailUpdatedUtc = CASE
                      WHEN NULLIF(@LoggedInUser, '') IS NOT NULL
                        OR NULLIF(@LanIp, '') IS NOT NULL
                        OR NULLIF(@PublicIp, '') IS NOT NULL
                        OR NULLIF(@MacAddress, '') IS NOT NULL
                        OR NULLIF(@Domain, '') IS NOT NULL
                        OR NULLIF(@WindowsVersion, '') IS NOT NULL
                        OR NULLIF(@SerialNumber, '') IS NOT NULL
                        OR NULLIF(@CpuModel, '') IS NOT NULL
                        OR NULLIF(@RamTotal, '') IS NOT NULL
                        OR NULLIF(@DiskTotal, '') IS NOT NULL
                        OR NULLIF(@Mainboard, '') IS NOT NULL
                        OR NULLIF(@Uptime, '') IS NOT NULL
                      THEN SYSUTCDATETIME()
                      ELSE DetailUpdatedUtc
                  END,
                  LastSeenUtc = SYSUTCDATETIME()
              WHEN NOT MATCHED THEN
                  INSERT (DeviceName, LastSiteName, LastRegion, LastInternalSeenUtc, IsInternal, AppVersion,
                          CpuPercent, RamPercent, DiskPercent, NetworkLatencyMs,
                          NetworkType, WifiSignalDbm, LanLinkSpeed,
                          AdJoined, TrellixInstalled, DesktopCentralInstalled,
                          LoggedInUser, LanIp, PublicIp, MacAddress,
                          Domain, WindowsVersion, SerialNumber, CpuModel,
                          RamTotal, DiskTotal, Mainboard, Uptime, DetailUpdatedUtc, LastSeenUtc)
                  VALUES (@DeviceName, @SiteName,
                      CASE WHEN @IsInternal = 1 AND NULLIF(@Region, '') IS NOT NULL THEN @Region ELSE NULL END,
                      CASE WHEN @IsInternal = 1 AND NULLIF(@Region, '') IS NOT NULL THEN SYSUTCDATETIME() ELSE NULL END,
                      @IsInternal, @AppVersion,
                          @CpuPercent, @RamPercent, @DiskPercent, @NetworkLatencyMs,
                          @NetworkType,
                          CASE WHEN @NetworkType = 'WiFi' THEN @WifiSignalDbm ELSE NULL END,
                          CASE WHEN @NetworkType = 'LAN' THEN NULLIF(@LanLinkSpeed, '') ELSE NULL END,
                          @AdJoined, @TrellixInstalled, @DesktopCentralInstalled,
                          NULLIF(@LoggedInUser, ''), NULLIF(@LanIp, ''), NULLIF(@PublicIp, ''), NULLIF(@MacAddress, ''),
                          NULLIF(@Domain, ''), NULLIF(@WindowsVersion, ''), NULLIF(@SerialNumber, ''), NULLIF(@CpuModel, ''),
                          NULLIF(@RamTotal, ''), NULLIF(@DiskTotal, ''), NULLIF(@Mainboard, ''), NULLIF(@Uptime, ''),
                          CASE
                              WHEN NULLIF(@LoggedInUser, '') IS NOT NULL
                                OR NULLIF(@LanIp, '') IS NOT NULL
                                OR NULLIF(@PublicIp, '') IS NOT NULL
                                OR NULLIF(@MacAddress, '') IS NOT NULL
                                OR NULLIF(@Domain, '') IS NOT NULL
                                OR NULLIF(@WindowsVersion, '') IS NOT NULL
                                OR NULLIF(@SerialNumber, '') IS NOT NULL
                                OR NULLIF(@CpuModel, '') IS NOT NULL
                                OR NULLIF(@RamTotal, '') IS NOT NULL
                                OR NULLIF(@DiskTotal, '') IS NOT NULL
                                OR NULLIF(@Mainboard, '') IS NOT NULL
                                OR NULLIF(@Uptime, '') IS NOT NULL
                              THEN SYSUTCDATETIME()
                              ELSE NULL
                          END,
                          SYSUTCDATETIME());",
            new
            {
                DeviceName = effectiveDeviceName,
                SiteName = dto.SiteName,
                Region = dto.Region,
                IsInternal = effectiveIsInternal ? 1 : 0,
                HasNetworkData = !string.IsNullOrWhiteSpace(dto.LanIp) || !string.IsNullOrWhiteSpace(dto.PublicIp) ? 1 : 0,
                AppVersion = dto.AppVersion,
                CpuPercent = dto.CpuPercent,
                RamPercent = dto.RamPercent,
                DiskPercent = dto.DiskPercent,
                NetworkLatencyMs = dto.NetworkLatencyMs,
                NetworkType = dto.NetworkType,
                WifiSignalDbm = dto.WifiSignalDbm,
                LanLinkSpeed = dto.LanLinkSpeed,
                AdJoined = dto.AdJoined,
                TrellixInstalled = dto.TrellixInstalled,
                DesktopCentralInstalled = dto.DesktopCentralInstalled,
                LoggedInUser = dto.LoggedInUser,
                LanIp = dto.LanIp,
                PublicIp = dto.PublicIp,
                MacAddress = dto.MacAddress,
                Domain = dto.Domain,
                WindowsVersion = dto.WindowsVersion,
                SerialNumber = dto.SerialNumber,
                CpuModel = dto.CpuModel,
                RamTotal = dto.RamTotal,
                DiskTotal = dto.DiskTotal,
                Mainboard = dto.Mainboard,
                Uptime = dto.Uptime
            },
            transaction: tx);

        await UpdateDeviceHistoryAsync(conn, tx, resolvedDeviceId, dto);

        tx.Commit();
    }

    private async Task UpdateDeviceHistoryAsync(SqlConnection conn, SqlTransaction tx, Guid deviceId, HeartbeatDto dto)
    {
        var current = await conn.QuerySingleOrDefaultAsync<dynamic>(
            @"SELECT TOP 1 ComputerName, CurrentUser AS Username, CurrentDepartment AS Department, CurrentLocation AS Location, MACAddress
              FROM dbo.Devices
              WHERE DeviceId = @DeviceId",
            new { DeviceId = deviceId },
            transaction: tx);

        var newComputerName = string.IsNullOrWhiteSpace(dto.ComputerName) ? dto.DeviceName : dto.ComputerName;
        var newUsername = dto.Username;
        var newDepartment = dto.Department;
        var newLocation = dto.Location;
        var newMacAddress = dto.MacAddress;
        var currentComputerName = (string?)current?.ComputerName;
        var currentUsername = (string?)current?.Username;
        var currentDepartment = (string?)current?.Department;
        var currentLocation = (string?)current?.Location;
        var currentMacAddress = (string?)current?.MacAddress;

        var changed = !string.Equals(currentComputerName, newComputerName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(currentUsername, newUsername, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(currentDepartment, newDepartment, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(currentLocation, newLocation, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(currentMacAddress, newMacAddress, StringComparison.OrdinalIgnoreCase);

        if (!changed) return;

        await conn.ExecuteAsync(
            @"INSERT INTO dbo.DeviceHistory (DeviceId, MACAddress, ComputerName, Username, Department, Location, Timestamp, ChangeType)
              VALUES (@DeviceId, @MacAddress, @ComputerName, @Username, @Department, @Location, SYSUTCDATETIME(), 'DEVICE_INFO_CHANGED')",
            new
            {
                DeviceId = deviceId,
                MacAddress = newMacAddress,
                ComputerName = newComputerName,
                Username = newUsername,
                Department = newDepartment,
                Location = newLocation
            },
            transaction: tx);
    }

    public async Task RecordNetworkStatusAsync(NetworkStatusDto dto)
    {
        using var conn = CreateConnection();
        var deviceId = ResolveDeviceId(dto.DeviceId, dto.DeviceName, dto.MacAddress);

        await conn.ExecuteAsync(
            @"INSERT INTO dbo.NetworkStatus (DeviceId, ConnectionType, AdapterName, IPAddress, MACAddress, SSID, SignalStrengthDbm, LinkSpeedMbps, DownloadMbps, UploadMbps, Timestamp)
              VALUES (@DeviceId, @ConnectionType, @AdapterName, @IPAddress, @MacAddress, @SSID, @SignalStrengthDbm, @LinkSpeedMbps, @DownloadMbps, @UploadMbps, SYSUTCDATETIME())",
            new
            {
                DeviceId = deviceId,
                ConnectionType = dto.ConnectionType,
                AdapterName = dto.AdapterName,
                IPAddress = dto.IPAddress,
                MacAddress = dto.MacAddress,
                SSID = dto.SSID,
                SignalStrengthDbm = dto.SignalStrengthDbm,
                LinkSpeedMbps = dto.LinkSpeedMbps,
                DownloadMbps = dto.DownloadMbps,
                UploadMbps = dto.UploadMbps
            });
    }

    public async Task RecordPerformanceLogAsync(PerformanceLogDto dto)
    {
        using var conn = CreateConnection();
        var deviceId = ResolveDeviceId(dto.DeviceId, dto.DeviceName, null);

        await conn.ExecuteAsync(
            @"INSERT INTO dbo.PerformanceLogs (DeviceId, CPUUsage, RAMUsage, DiskUsage, DiskRead, DiskWrite, DiskIO, TopProcess, Timestamp)
              VALUES (@DeviceId, @CpuUsage, @RamUsage, @DiskUsage, @DiskRead, @DiskWrite, @DiskIO, @TopProcess, SYSUTCDATETIME())",
            new
            {
                DeviceId = deviceId,
                CpuUsage = dto.CpuUsage,
                RamUsage = dto.RamUsage,
                DiskUsage = dto.DiskUsage,
                DiskRead = dto.DiskRead,
                DiskWrite = dto.DiskWrite,
                DiskIO = dto.DiskIO,
                TopProcess = dto.TopProcess
            });
    }

    public async Task RecordComplianceStatusAsync(ComplianceStatusDto dto)
    {
        using var conn = CreateConnection();
        var deviceId = ResolveDeviceId(dto.DeviceId, dto.DeviceName, null);

        await conn.ExecuteAsync(
            @"INSERT INTO dbo.ComplianceStatus (DeviceId, Antivirus, Firewall, WindowsUpdate, BitLocker, PasswordPolicy, EndpointProtection, OverallStatus, Timestamp)
              VALUES (@DeviceId, @Antivirus, @Firewall, @WindowsUpdate, @BitLocker, @PasswordPolicy, @EndpointProtection, @OverallStatus, SYSUTCDATETIME())",
            new
            {
                DeviceId = deviceId,
                Antivirus = dto.Antivirus,
                Firewall = dto.Firewall,
                WindowsUpdate = dto.WindowsUpdate,
                BitLocker = dto.BitLocker,
                PasswordPolicy = dto.PasswordPolicy,
                EndpointProtection = dto.EndpointProtection,
                OverallStatus = dto.OverallStatus
            });
    }

    public async Task UpsertSoftwareInventoryAsync(SoftwareInventoryEntryDto dto)
    {
        using var conn = CreateConnection();

        // Try to find existing device by ComputerName (case-insensitive) using the Devices table.
        var existingDeviceId = await conn.ExecuteScalarAsync<Guid?>(
            @"SELECT TOP 1 DeviceId FROM dbo.Devices WHERE LOWER(ComputerName) = LOWER(@DeviceName) ORDER BY LastSeen DESC",
            new { DeviceName = dto.DeviceName });

        var deviceId = existingDeviceId ?? ResolveDeviceId(dto.DeviceId, dto.DeviceName, null);

        await conn.ExecuteAsync(
            @"MERGE dbo.SoftwareInventory AS target
              USING (SELECT @DeviceId AS DeviceId, @SoftwareName AS SoftwareName) AS src
              ON target.DeviceId = src.DeviceId AND target.SoftwareName = src.SoftwareName
              WHEN MATCHED THEN UPDATE SET Version = COALESCE(NULLIF(@Version, ''), Version), Publisher = COALESCE(NULLIF(@Publisher, ''), Publisher), InstallDate = COALESCE(@InstallDate, InstallDate), LastDetected = SYSUTCDATETIME()
              WHEN NOT MATCHED THEN INSERT (DeviceId, SoftwareName, Version, Publisher, InstallDate, FirstDetected, LastDetected)
              VALUES (@DeviceId, @SoftwareName, NULLIF(@Version, ''), NULLIF(@Publisher, ''), @InstallDate, SYSUTCDATETIME(), SYSUTCDATETIME());",
            new
            {
                DeviceId = deviceId,
                SoftwareName = dto.SoftwareName,
                Version = dto.Version,
                Publisher = dto.Publisher,
                InstallDate = dto.InstallDate
            });
    }

    public async Task UpsertSoftwareInventoryBulkAsync(IEnumerable<SoftwareInventoryEntryDto> dtos)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            var groups = dtos
                .Where(d => d != null && !string.IsNullOrWhiteSpace(d.SoftwareName) && !string.IsNullOrWhiteSpace(d.DeviceName))
                .GroupBy(d => d.DeviceName.Trim(), StringComparer.OrdinalIgnoreCase);

            foreach (var grp in groups)
            {
                var deviceName = grp.Key;
                var existingDeviceId = await conn.ExecuteScalarAsync<Guid?>(
                    @"SELECT TOP 1 DeviceId FROM dbo.Devices WHERE LOWER(ComputerName) = LOWER(@DeviceName) ORDER BY LastSeen DESC",
                    new { DeviceName = deviceName }, transaction: tx);

                Guid deviceId = existingDeviceId ?? ResolveDeviceId(null, deviceName, null);

                foreach (var item in grp)
                {
                    await conn.ExecuteAsync(
                        @"MERGE dbo.SoftwareInventory AS target
                          USING (SELECT @DeviceId AS DeviceId, @SoftwareName AS SoftwareName) AS src
                          ON target.DeviceId = src.DeviceId AND target.SoftwareName = src.SoftwareName
                          WHEN MATCHED THEN UPDATE SET Version = COALESCE(NULLIF(@Version, ''), Version), Publisher = COALESCE(NULLIF(@Publisher, ''), Publisher), InstallDate = COALESCE(@InstallDate, InstallDate), LastDetected = SYSUTCDATETIME()
                          WHEN NOT MATCHED THEN INSERT (DeviceId, SoftwareName, Version, Publisher, InstallDate, FirstDetected, LastDetected)
                          VALUES (@DeviceId, @SoftwareName, NULLIF(@Version, ''), NULLIF(@Publisher, ''), @InstallDate, SYSUTCDATETIME(), SYSUTCDATETIME());",
                        new
                        {
                            DeviceId = deviceId,
                            SoftwareName = item.SoftwareName,
                            Version = item.Version,
                            Publisher = item.Publisher,
                            InstallDate = item.InstallDate
                        }, transaction: tx);
                }
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateDevicePreferenceAsync(DevicePreferenceRequestDto dto)
    {
        using var conn = CreateConnection();
        var deviceId = ResolveDeviceId(dto.DeviceId, dto.DeviceName, null);
        var action = dto.Action ?? "dismiss";

        // Quy tắc mong muốn:
        // - "dismiss" hoặc "hide" => tắt cảnh báo sau này trên máy user
        // - "close" => chỉ đóng popup hiện tại, không lưu trạng thái tắt vĩnh viễn
        // - Dashboard vẫn giữ nguyên nhận cảnh báo bình thường
        if (string.Equals(action, "close", StringComparison.OrdinalIgnoreCase))
            return;

        var disableWarning = string.Equals(action, "dismiss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "hide", StringComparison.OrdinalIgnoreCase);

        await conn.ExecuteAsync(
            @"UPDATE dbo.Devices
              SET NetworkWarningDisabled = @DisableWarning,
                  NetworkWarningDisabledAt = CASE WHEN @DisableWarning = 1 THEN SYSUTCDATETIME() ELSE NULL END
              WHERE DeviceId = @DeviceId",
            new { DeviceId = deviceId, DisableWarning = disableWarning ? 1 : 0 });
    }

    public async Task<AgentStatusDto?> GetAgentStatusAsync(string? deviceNameOrId, decimal cpuWarningPercent, decimal ramWarningPercent, decimal diskWarningPercent, decimal diskCriticalPercent, int diskIoWarning)
    {
        using var conn = CreateConnection();

        Guid? parsedDeviceId = null;
        if (Guid.TryParse(deviceNameOrId, out var deviceId)) parsedDeviceId = deviceId;

        var device = await conn.QuerySingleOrDefaultAsync<(Guid DeviceId, string DeviceName, bool? NetworkWarningDisabled, string? Status, string? CurrentUser)>(
            @"SELECT TOP 1 DeviceId, ComputerName AS DeviceName, NetworkWarningDisabled, Status, CurrentUser
              FROM dbo.Devices
              WHERE (@DeviceId IS NOT NULL AND DeviceId = @DeviceId)
                 OR (@DeviceName IS NOT NULL AND ComputerName = @DeviceName)
              ORDER BY LastSeen DESC",
            new { DeviceId = parsedDeviceId, DeviceName = deviceNameOrId });

        if (device == default) return null;

        var network = await conn.QuerySingleOrDefaultAsync<dynamic>(
            @"SELECT TOP 1 ConnectionType, SSID, SignalStrengthDbm
              FROM dbo.NetworkStatus
              WHERE DeviceId = @DeviceId
              ORDER BY Timestamp DESC",
            new { DeviceId = device.DeviceId });

        var performance = await conn.QuerySingleOrDefaultAsync<dynamic>(
            @"SELECT TOP 1 CPUUsage, RAMUsage, DiskUsage, DiskIO
              FROM dbo.PerformanceLogs
              WHERE DeviceId = @DeviceId
              ORDER BY Timestamp DESC",
            new { DeviceId = device.DeviceId });

        var complianceStatus = await conn.QuerySingleOrDefaultAsync<string?>(
            @"SELECT TOP 1 OverallStatus
              FROM dbo.ComplianceStatus
              WHERE DeviceId = @DeviceId
              ORDER BY Timestamp DESC",
            new { DeviceId = device.DeviceId });

        bool hasExternalNetworkWarning = !string.Equals(device.Status, "Active", StringComparison.OrdinalIgnoreCase) && !(device.NetworkWarningDisabled ?? false);
        bool hasSecurityWarning = !string.Equals(complianceStatus, "Compliant", StringComparison.OrdinalIgnoreCase);
        var cpuUsage = (decimal?)(performance?.CpuUsage ?? 0m);
        var ramUsage = (decimal?)(performance?.RamUsage ?? 0m);
        var diskUsage = (decimal?)(performance?.DiskUsage ?? 0m);
        var diskIo = (decimal?)(performance?.DiskIO ?? 0m);
        bool hasPerformanceWarning = (cpuUsage ?? 0m) >= cpuWarningPercent ||
                                     (ramUsage ?? 0m) >= ramWarningPercent ||
                                     (diskUsage ?? 0m) >= diskWarningPercent ||
                                     (diskIo ?? 0m) >= diskIoWarning;

        var messages = new List<string>();
        if (hasExternalNetworkWarning) messages.Add("Mạng ngoài");
        if (hasSecurityWarning) messages.Add("Bảo mật");
        if (hasPerformanceWarning) messages.Add("Hiệu năng");

        return new AgentStatusDto
        {
            DeviceName = device.DeviceName,
            DeviceId = device.DeviceId,
            IsOk = messages.Count == 0,
            IsExpanded = false,
            NetworkWarningDisabled = device.NetworkWarningDisabled ?? false,
            HasExternalNetworkWarning = hasExternalNetworkWarning,
            HasSecurityWarning = hasSecurityWarning,
            HasPerformanceWarning = hasPerformanceWarning,
            Summary = messages.Count == 0 ? "Không có cảnh báo" : string.Join(" • ", messages),
            NetworkWarningMessage = hasExternalNetworkWarning
                ? "Bạn đang sử dụng mạng ngoài của tập đoàn. Khi sử dụng mạng ngoài, một số tính năng nội bộ sẽ không sử dụng được."
                : null,
            SecurityWarningMessage = hasSecurityWarning
                ? "Máy tính của bạn hiện chưa đảm bảo tuân thủ an ninh bảo mật. Vui lòng liên hệ IT Site để được hỗ trợ."
                : null,
            PerformanceWarningMessage = hasPerformanceWarning
                ? "Máy tính đang hoạt động quá tải. Vui lòng tắt bớt các ứng dụng không cần thiết."
                : null,
            ComplianceStatus = complianceStatus ?? "Unknown",
            ConnectionType = (string?)network?.ConnectionType,
            Ssid = (string?)network?.SSID,
            SignalStrengthDbm = (double?)(network?.SignalStrengthDbm ?? 0d),
            CurrentUser = device.CurrentUser,
            IsInternal = string.Equals(device.Status, "Active", StringComparison.OrdinalIgnoreCase)
        };
    }

    // ---------------- Dashboard ----------------

    public async Task<IEnumerable<DeviceDashboardDto>> GetDashboardDevicesAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<DeviceDashboardDto>(
            @"WITH LatestHeartbeat AS (
                   SELECT h.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(h.DeviceName) ORDER BY h.LastSeenUtc DESC, h.DeviceName) AS RowNum
                   FROM dbo.DeviceHeartbeats h
              ),
              LatestDevice AS (
                   SELECT d.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                   FROM dbo.Devices d
              )
              SELECT h.DeviceName,
                     d.DeviceId,
                     d.MACAddress,
                     d.CurrentUser,
                     d.CurrentDepartment,
                     d.CurrentLocation,
                     d.NetworkWarningDisabled,
                     CASE WHEN h.IsInternal = 1 THEN h.LastSiteName ELSE NULL END AS LastSiteName,
                     h.LastRegion,
                     h.IsInternal,
                     h.CpuPercent,
                     h.RamPercent,
                     h.DiskPercent,
                     h.NetworkLatencyMs,
                     h.NetworkType,
                     h.WifiSignalDbm,
                     h.LanLinkSpeed,
                     h.AdJoined,
                     h.TrellixInstalled,
                     h.DesktopCentralInstalled,
                     h.AppVersion,
                     h.LastSeenUtc,
                    CASE WHEN h.LastSeenUtc >= DATEADD(MINUTE, -2, SYSUTCDATETIME()) THEN 1 ELSE 0 END AS IsOnline,
                     (SELECT COUNT(*) FROM dbo.PerformanceWarnings w WHERE w.DeviceName = h.DeviceName AND w.WarnedAtUtc >= CAST(SYSUTCDATETIME() AS DATE)) AS WarningsToday,
                     c.OverallStatus AS ComplianceStatus,
                     CASE WHEN COALESCE(h.IsInternal, 0) = 1 THEN 'Internal' ELSE 'External' END AS ExternalNetworkStatus
              FROM LatestHeartbeat h
              LEFT JOIN LatestDevice d ON LOWER(d.ComputerName) = LOWER(h.DeviceName) AND d.RowNum = 1
              LEFT JOIN (
                  SELECT DeviceId, OverallStatus
                  FROM (
                      SELECT DeviceId, OverallStatus, ROW_NUMBER() OVER (PARTITION BY DeviceId ORDER BY Timestamp DESC) AS RowNum
                      FROM dbo.ComplianceStatus
                  ) x
                  WHERE RowNum = 1
              ) c ON c.DeviceId = d.DeviceId
              WHERE h.RowNum = 1
              ORDER BY h.LastSeenUtc DESC");
    }

    public async Task<IEnumerable<DeviceReportWarningDto>> GetTodayWarningsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<DeviceReportWarningDto>(
            @"SELECT DeviceName, MetricType, MetricValue, WarnedAtUtc, SiteName, Region
              FROM dbo.PerformanceWarnings
              WHERE WarnedAtUtc >= CAST(SYSUTCDATETIME() AS DATE)
              ORDER BY WarnedAtUtc DESC");
    }

    // ---------------- Weekly report (dùng lại trong WeeklyReportJob) ----------------

    public async Task<IEnumerable<dynamic>> GetWeeklyReportAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync("SELECT * FROM dbo.vw_WeeklyPerformanceReport ORDER BY TotalWarningCount DESC");
    }

    public async Task<DeviceReportDataDto> GetDeviceReportAsync(string deviceName)
    {
        using var conn = CreateConnection();

        var device = await conn.QuerySingleOrDefaultAsync<DeviceDashboardDto>(
            @"WITH LatestHeartbeat AS (
                   SELECT h.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(h.DeviceName) ORDER BY h.LastSeenUtc DESC, h.DeviceName) AS RowNum
                   FROM dbo.DeviceHeartbeats h
                   WHERE LOWER(h.DeviceName) = LOWER(@DeviceName)
               ),
               LatestDevice AS (
                   SELECT d.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                   FROM dbo.Devices d
                   WHERE LOWER(d.ComputerName) = LOWER(@DeviceName)
               )
               SELECT TOP 1 h.DeviceName,
                     d.DeviceId,
                     d.MACAddress,
                     d.CurrentUser,
                     d.CurrentDepartment,
                     d.CurrentLocation,
                     d.NetworkWarningDisabled,
                     CASE WHEN h.IsInternal = 1 THEN h.LastSiteName ELSE NULL END AS LastSiteName,
                     h.LastRegion,
                     h.IsInternal,
                     h.CpuPercent,
                     h.RamPercent,
                     h.DiskPercent,
                     h.NetworkLatencyMs,
                     h.NetworkType,
                     h.WifiSignalDbm,
                     h.LanLinkSpeed,
                     h.AdJoined,
                     h.TrellixInstalled,
                     h.DesktopCentralInstalled,
                     h.AppVersion,
                     h.LastSeenUtc,
                    CASE WHEN h.LastSeenUtc >= DATEADD(MINUTE, -2, SYSUTCDATETIME()) THEN 1 ELSE 0 END AS IsOnline,
                     (SELECT COUNT(*) FROM dbo.PerformanceWarnings w WHERE LOWER(w.DeviceName) = LOWER(@DeviceName) AND w.WarnedAtUtc >= CAST(SYSUTCDATETIME() AS DATE)) AS WarningsToday,
                     c.OverallStatus AS ComplianceStatus,
                     CASE WHEN COALESCE(h.IsInternal, 0) = 1 THEN 'Internal' ELSE 'External' END AS ExternalNetworkStatus
              FROM LatestHeartbeat h
              LEFT JOIN LatestDevice d ON LOWER(d.ComputerName) = LOWER(h.DeviceName) AND d.RowNum = 1
              LEFT JOIN (
                  SELECT DeviceId, OverallStatus
                  FROM (
                      SELECT DeviceId, OverallStatus, ROW_NUMBER() OVER (PARTITION BY DeviceId ORDER BY Timestamp DESC) AS RowNum
                      FROM dbo.ComplianceStatus
                  ) x
                  WHERE RowNum = 1
              ) c ON c.DeviceId = d.DeviceId
              WHERE h.RowNum = 1
              ORDER BY h.LastSeenUtc DESC",
            new { DeviceName = deviceName });

        var warnings = (await conn.QueryAsync<DeviceReportWarningDto>(
            @"SELECT DeviceName, MetricType, MetricValue, WarnedAtUtc, SiteName, Region
              FROM dbo.PerformanceWarnings
              WHERE LOWER(DeviceName) = LOWER(@DeviceName)
                AND WarnedAtUtc >= DATEADD(DAY, -7, SYSUTCDATETIME())
              ORDER BY WarnedAtUtc DESC",
            new { DeviceName = deviceName })).ToList();

        return new DeviceReportDataDto
        {
            Device = device,
            Warnings = warnings
        };
    }

    public async Task<(List<DeviceDashboardDto> Devices, List<DeviceReportWarningDto> Warnings)> GetMultiDeviceReportAsync(List<string>? deviceNames)
    {
        using var conn = CreateConnection();

        var hasFilter = deviceNames is { Count: > 0 };
        var devicesSql = hasFilter
            ? @"WITH LatestHeartbeat AS (
                   SELECT h.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(h.DeviceName) ORDER BY h.LastSeenUtc DESC, h.DeviceName) AS RowNum
                   FROM dbo.DeviceHeartbeats h
                   WHERE h.DeviceName IN @DeviceNames
               ),
               LatestDevice AS (
                   SELECT d.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                   FROM dbo.Devices d
               )
               SELECT h.DeviceName,
                     d.DeviceId,
                     d.MACAddress,
                     d.CurrentUser,
                     d.CurrentDepartment,
                     d.CurrentLocation,
                     d.NetworkWarningDisabled,
                     CASE WHEN h.IsInternal = 1 THEN h.LastSiteName ELSE NULL END AS LastSiteName,
                     h.LastRegion,
                     h.IsInternal,
                     h.CpuPercent,
                     h.RamPercent,
                     h.DiskPercent,
                     h.NetworkLatencyMs,
                     h.NetworkType,
                     h.WifiSignalDbm,
                     h.LanLinkSpeed,
                     h.AdJoined,
                     h.TrellixInstalled,
                     h.DesktopCentralInstalled,
                     h.AppVersion,
                     h.LastSeenUtc,
                     CASE WHEN h.LastSeenUtc >= DATEADD(MINUTE, -2, SYSUTCDATETIME()) THEN 1 ELSE 0 END AS IsOnline,
                     (SELECT COUNT(*) FROM dbo.PerformanceWarnings w WHERE w.DeviceName = h.DeviceName AND w.WarnedAtUtc >= CAST(SYSUTCDATETIME() AS DATE)) AS WarningsToday,
                     c.OverallStatus AS ComplianceStatus,
                     CASE WHEN COALESCE(h.IsInternal, 0) = 1 THEN 'Internal' ELSE 'External' END AS ExternalNetworkStatus
               FROM LatestHeartbeat h
               LEFT JOIN LatestDevice d ON LOWER(d.ComputerName) = LOWER(h.DeviceName) AND d.RowNum = 1
               LEFT JOIN (
                  SELECT DeviceId, OverallStatus
                  FROM (
                      SELECT DeviceId, OverallStatus, ROW_NUMBER() OVER (PARTITION BY DeviceId ORDER BY Timestamp DESC) AS RowNum
                      FROM dbo.ComplianceStatus
                  ) x
                  WHERE RowNum = 1
               ) c ON c.DeviceId = d.DeviceId
               WHERE h.RowNum = 1
               ORDER BY h.LastSeenUtc DESC"
            : @"WITH LatestHeartbeat AS (
                   SELECT h.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(h.DeviceName) ORDER BY h.LastSeenUtc DESC, h.DeviceName) AS RowNum
                   FROM dbo.DeviceHeartbeats h
               ),
               LatestDevice AS (
                   SELECT d.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                   FROM dbo.Devices d
               )
               SELECT h.DeviceName,
                     d.DeviceId,
                     d.MACAddress,
                     d.CurrentUser,
                     d.CurrentDepartment,
                     d.CurrentLocation,
                     d.NetworkWarningDisabled,
                     CASE WHEN h.IsInternal = 1 THEN h.LastSiteName ELSE NULL END AS LastSiteName,
                     h.LastRegion,
                     h.IsInternal,
                     h.CpuPercent,
                     h.RamPercent,
                     h.DiskPercent,
                     h.NetworkLatencyMs,
                     h.NetworkType,
                     h.WifiSignalDbm,
                     h.LanLinkSpeed,
                     h.AdJoined,
                     h.TrellixInstalled,
                     h.DesktopCentralInstalled,
                     h.AppVersion,
                     h.LastSeenUtc,
                     CASE WHEN h.LastSeenUtc >= DATEADD(MINUTE, -2, SYSUTCDATETIME()) THEN 1 ELSE 0 END AS IsOnline,
                     (SELECT COUNT(*) FROM dbo.PerformanceWarnings w WHERE w.DeviceName = h.DeviceName AND w.WarnedAtUtc >= CAST(SYSUTCDATETIME() AS DATE)) AS WarningsToday,
                     c.OverallStatus AS ComplianceStatus,
                     CASE WHEN COALESCE(h.IsInternal, 0) = 1 THEN 'Internal' ELSE 'External' END AS ExternalNetworkStatus
               FROM LatestHeartbeat h
               LEFT JOIN LatestDevice d ON LOWER(d.ComputerName) = LOWER(h.DeviceName) AND d.RowNum = 1
               LEFT JOIN (
                  SELECT DeviceId, OverallStatus
                  FROM (
                      SELECT DeviceId, OverallStatus, ROW_NUMBER() OVER (PARTITION BY DeviceId ORDER BY Timestamp DESC) AS RowNum
                      FROM dbo.ComplianceStatus
                  ) x
                  WHERE RowNum = 1
               ) c ON c.DeviceId = d.DeviceId
               WHERE h.RowNum = 1
               ORDER BY h.LastSeenUtc DESC";

        var warningsSql = hasFilter
            ? @"SELECT DeviceName, MetricType, MetricValue, WarnedAtUtc, SiteName, Region
                FROM dbo.PerformanceWarnings
                WHERE DeviceName IN @DeviceNames
                  AND WarnedAtUtc >= DATEADD(DAY, -7, SYSUTCDATETIME())
                ORDER BY DeviceName, WarnedAtUtc DESC"
            : @"SELECT DeviceName, MetricType, MetricValue, WarnedAtUtc, SiteName, Region
                FROM dbo.PerformanceWarnings
                WHERE WarnedAtUtc >= DATEADD(DAY, -7, SYSUTCDATETIME())
                ORDER BY DeviceName, WarnedAtUtc DESC";

        var devices = (await conn.QueryAsync<DeviceDashboardDto>(devicesSql, new { DeviceNames = deviceNames })).ToList();
        var warnings = (await conn.QueryAsync<DeviceReportWarningDto>(warningsSql, new { DeviceNames = deviceNames })).ToList();

        return (devices, warnings);
    }

    public async Task<DeviceDetailDto?> GetDeviceDetailAsync(string deviceName)
    {
        using var conn = CreateConnection();
        var device = await conn.QuerySingleOrDefaultAsync<DeviceDetailDto>(
            @"WITH LatestHeartbeat AS (
                   SELECT h.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(h.DeviceName) ORDER BY h.LastSeenUtc DESC, h.DeviceName) AS RowNum
                   FROM dbo.DeviceHeartbeats h
               ),
               LatestDevice AS (
                   SELECT d.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                   FROM dbo.Devices d
               )
               SELECT TOP 1
                   d.ComputerName AS DeviceName,
                   d.DeviceId,
                   d.CurrentUser,
                   d.CurrentDepartment,
                   d.CurrentLocation,
                   CASE WHEN COALESCE(h.IsInternal, 0) = 1 THEN 'Active' ELSE 'External' END AS CurrentStatus,
                   d.NetworkWarningDisabled,
                   d.MACAddress,
                   h.LoggedInUser,
                   h.LanIp,
                   h.PublicIp,
                   h.Domain,
                   h.WindowsVersion,
                   h.SerialNumber,
                   h.CpuModel,
                   h.RamTotal,
                   h.DiskTotal,
                   h.Mainboard,
                   h.Uptime,
                   h.AppVersion,
                   h.DetailUpdatedUtc,
                   h.LastSeenUtc,
                   CASE WHEN COALESCE(h.IsInternal, 0) = 1 THEN 'Internal' ELSE 'External' END AS ExternalNetworkStatus,
                CASE WHEN h.LastSeenUtc >= DATEADD(MINUTE, -2, SYSUTCDATETIME()) THEN 1 ELSE 0 END AS IsOnline
               FROM LatestDevice d
               LEFT JOIN LatestHeartbeat h ON LOWER(h.DeviceName) = LOWER(d.ComputerName) AND h.RowNum = 1
               WHERE LOWER(d.ComputerName) = LOWER(@DeviceName)
                 AND d.RowNum = 1",
            new { DeviceName = deviceName });

        if (device == null) return null;

        var complianceStatus = await conn.QuerySingleOrDefaultAsync<string?>(
            @"SELECT TOP 1 OverallStatus
              FROM dbo.ComplianceStatus
              WHERE DeviceId = @DeviceId
              ORDER BY Timestamp DESC",
            new { DeviceId = device.DeviceId });

        device.ComplianceStatus = complianceStatus;
        return device;
    }

    public async Task<List<SoftwareInventoryEntryDto>> GetSoftwareInventoryAsync(string deviceName)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var deviceId = await conn.ExecuteScalarAsync<Guid?>(
                @"WITH LatestDevice AS (
                        SELECT d.*,
                               ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                        FROM dbo.Devices d
                    )
                    SELECT TOP 1 DeviceId
                    FROM LatestDevice
                    WHERE LOWER(ComputerName) = LOWER(@DeviceName)
                      AND RowNum = 1
                    ORDER BY LastSeen DESC",
                new { DeviceName = deviceName });

            if (deviceId is null)
                return new List<SoftwareInventoryEntryDto>();

            return (await conn.QueryAsync<SoftwareInventoryEntryDto>(
                @"SELECT s.SoftwareName, s.Version, s.Publisher, s.InstallDate,
                         d.ComputerName AS DeviceName,
                         CAST(s.DeviceId AS VARCHAR(36)) AS DeviceId
                  FROM dbo.SoftwareInventory s
                  INNER JOIN dbo.Devices d ON d.DeviceId = s.DeviceId
                  WHERE s.DeviceId = @DeviceId
                  ORDER BY s.SoftwareName",
                new { DeviceId = deviceId })).ToList();
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "software_error.log"), 
                $"{DateTime.UtcNow:O} - Error fetching software for {deviceName}: {ex.Message}\n{ex.StackTrace}\n");
            throw;
        }
    }
}
