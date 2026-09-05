using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;
using SGRNetGuard.Api.Models;

namespace SGRNetGuard.Api.Data;

public class SqlDataAccess
{
    private readonly string _connectionString;

    public SqlDataAccess(IConfiguration config)
    {
        var databaseUrl = config["DATABASE_URL"];
        var configuredConnectionString = config.GetConnectionString("SGRNetGuard");
        _connectionString = ConvertRenderDatabaseUrl(databaseUrl)
            ?? configuredConnectionString
            ?? throw new InvalidOperationException("Thiếu ConnectionStrings:SGRNetGuard hoặc DATABASE_URL");
    }

    private static string? ConvertRenderDatabaseUrl(string? databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
            return null;

        if (!databaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !databaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return databaseUrl;

        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length != 2)
            throw new InvalidOperationException("DATABASE_URL PostgreSQL thiếu username hoặc password");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = Uri.UnescapeDataString(userInfo[1]),
            Database = uri.AbsolutePath.TrimStart('/'),
            SslMode = SslMode.Require
        };

        return builder.ConnectionString;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task EnsureDatabaseAndSiteCatalogAsync(string baseDirectory)
    {
        var schemaPath = Path.Combine(baseDirectory, "Database", "postgresql_schema.sql");
        var seedPath = Path.Combine(baseDirectory, "Database", "postgresql_seed.sql");
        if (!File.Exists(schemaPath) || !File.Exists(seedPath))
            throw new FileNotFoundException("Không tìm thấy file bootstrap PostgreSQL trong bản publish.");

        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var command = conn.CreateCommand();
        command.CommandTimeout = 60;

        command.CommandText = await File.ReadAllTextAsync(schemaPath);
        await command.ExecuteNonQueryAsync();
        command.CommandText = await File.ReadAllTextAsync(seedPath);
        await command.ExecuteNonQueryAsync();
    }

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

    private async Task<SiteDto?> ResolveSiteAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string? lanIp)
    {
        if (!IPAddress.TryParse(lanIp, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            return null;

        var sites = await conn.QueryAsync<SiteDto>(
            @"SELECT Region, SiteName AS Site, Subnet, ItAccount AS IT, TeamsAccount AS Teams
                            FROM public.Sites WHERE IsActive = TRUE",
                        transaction: tx);

        return sites
            .Where(site => IsIpInSubnet(address, site.Subnet))
            .OrderByDescending(site => GetSubnetPrefixLength(site.Subnet))
            .FirstOrDefault();
    }

    private static int GetSubnetPrefixLength(string? subnet)
    {
        var parts = subnet?.Split('/', 2, StringSplitOptions.TrimEntries);
        return parts is { Length: 2 } && int.TryParse(parts[1], out var prefixLength) ? prefixLength : -1;
    }

    private static bool IsIpInSubnet(IPAddress address, string? subnet)
    {
        var parts = subnet?.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 } ||
            !IPAddress.TryParse(parts[0], out var network) ||
            !int.TryParse(parts[1], out var prefixLength) ||
            network.AddressFamily != AddressFamily.InterNetwork ||
            prefixLength is < 0 or > 32)
            return false;

        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var index = 0; index < fullBytes; index++)
        {
            if (addressBytes[index] != networkBytes[index]) return false;
        }

        return remainingBits == 0 ||
               (addressBytes[fullBytes] & (byte)(0xff << (8 - remainingBits))) ==
               (networkBytes[fullBytes] & (byte)(0xff << (8 - remainingBits)));
    }

    public async Task<RemoteConfigDto> GetActiveConfigAsync()
    {
        using var conn = CreateConnection();

        var sites = (await conn.QueryAsync<SiteDto>(
              @"SELECT Region, SiteName AS Site, Subnet, ItAccount AS IT, TeamsAccount AS Teams
              FROM public.Sites WHERE IsActive = TRUE ORDER BY Region, SiteName")).ToList();

        var dnsRows = await conn.QueryAsync<(string Region, string DnsServer)>(
            "SELECT Region, DnsServer FROM public.RegionDnsServers");

        var dnsByRegion = dnsRows
            .GroupBy(r => r.Region)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DnsServer).ToList());

        var ssid = await conn.ExecuteScalarAsync<string>(
            "SELECT Value FROM public.AppSettings WHERE Key = 'ValidSsid'") ?? "SGR-OFFICE";

        var version = await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT MAX(UpdatedAtUtc) FROM public.Sites");

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
                        @"INSERT INTO public.PerformanceWarnings (DeviceName, SiteName, Region, MetricType, MetricValue)
                            VALUES (@DeviceName, @SiteName, @Region, @MetricType, @MetricValue)",
            dto);

        await EnsureWarningAlertAsync(conn, dto);
    }

    private async Task EnsureWarningAlertAsync(NpgsqlConnection conn, PerformanceWarningDto dto)
    {
        var deviceName = dto.DeviceName;
        var title = dto.MetricType switch
        {
            "CPU" => "Cảnh báo CPU",
            "RAM" => "Cảnh báo RAM",
            _ => "Cảnh báo ổ cứng"
        };

        await conn.ExecuteAsync(
            @"INSERT INTO public.Alerts (DeviceId, AlertType, Severity, Title, Message, IsAcknowledged, CreatedAt)
              SELECT DeviceId, @AlertType, @Severity, @Title, @Message, FALSE, CURRENT_TIMESTAMP
              FROM public.Devices
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

        var resolvedDeviceId = ResolveDeviceId(dto.DeviceId, dto.DeviceName, dto.MacAddress);
        var effectiveDeviceName = dto.DeviceName;
        var resolvedSite = await ResolveSiteAsync(conn, tx, dto.LanIp);
        var hasAppSite = !string.IsNullOrWhiteSpace(dto.SiteName) && !string.IsNullOrWhiteSpace(dto.Region);
        // A site/region reported by the desktop app is the source of truth for the
        // dashboard. The client may legitimately have a different DNS configuration
        // at a site, so dto.IsInternal must not discard a valid app site.
        var effectiveIsInternal = hasAppSite || resolvedSite is not null;
        var effectiveSiteName = hasAppSite ? dto.SiteName : resolvedSite?.Site;
        var effectiveRegion = hasAppSite ? dto.Region : resolvedSite?.Region;

        await conn.ExecuteAsync(
            @"INSERT INTO public.Devices (DeviceId, MACAddress, ComputerName, CurrentUser, CurrentDepartment, CurrentLocation, OperatingSystem, FirstSeen, LastSeen, Status)
              VALUES (@DeviceId, NULLIF(@MacAddress, ''), NULLIF(@ComputerName, ''), NULLIF(@Username, ''), NULLIF(@Department, ''), NULLIF(@Location, ''), NULLIF(@OperatingSystem, ''), CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CASE WHEN @IsInternal THEN 'Active' ELSE 'External' END)
              ON CONFLICT (DeviceId) DO UPDATE SET
                  MACAddress = COALESCE(NULLIF(EXCLUDED.MACAddress, ''), public.Devices.MACAddress),
                  ComputerName = COALESCE(NULLIF(EXCLUDED.ComputerName, ''), public.Devices.ComputerName),
                  CurrentUser = COALESCE(NULLIF(EXCLUDED.CurrentUser, ''), public.Devices.CurrentUser),
                  CurrentDepartment = COALESCE(NULLIF(EXCLUDED.CurrentDepartment, ''), public.Devices.CurrentDepartment),
                  CurrentLocation = COALESCE(NULLIF(EXCLUDED.CurrentLocation, ''), public.Devices.CurrentLocation),
                  OperatingSystem = COALESCE(NULLIF(EXCLUDED.OperatingSystem, ''), public.Devices.OperatingSystem),
                  LastSeen = CURRENT_TIMESTAMP,
                  Status = CASE WHEN @IsInternal THEN 'Active' ELSE 'External' END;",
            new
            {
                DeviceId = resolvedDeviceId,
                MacAddress = dto.MacAddress,
                ComputerName = effectiveDeviceName,
                Username = dto.Username,
                Department = dto.Department,
                Location = dto.Location,
                OperatingSystem = dto.OperatingSystem,
                IsInternal = effectiveIsInternal,
                HasNetworkData = !string.IsNullOrWhiteSpace(dto.LanIp) || !string.IsNullOrWhiteSpace(dto.PublicIp)
            },
            transaction: tx);

        await conn.ExecuteAsync(
            @"INSERT INTO public.DeviceHeartbeats (DeviceName, LastSiteName, LastRegion, LastInternalSeenUtc, IsInternal, AppVersion,
                          CpuPercent, RamPercent, DiskPercent, NetworkLatencyMs,
                          NetworkType, WifiSignalDbm, LanLinkSpeed,
                          AdJoined, TrellixInstalled, DesktopCentralInstalled,
                          LoggedInUser, LanIp, PublicIp, MacAddress,
                          Domain, WindowsVersion, SerialNumber, CpuModel,
                          RamTotal, DiskTotal, Mainboard, Uptime, DetailUpdatedUtc, LastSeenUtc)
                  VALUES (@DeviceName, @SiteName,
                      CASE WHEN @IsInternal AND NULLIF(@Region, '') IS NOT NULL THEN @Region ELSE NULL END,
                      CASE WHEN @IsInternal AND NULLIF(@Region, '') IS NOT NULL THEN CURRENT_TIMESTAMP ELSE NULL END,
                      @IsInternal, @AppVersion,
                          @CpuPercent, @RamPercent, @DiskPercent, @NetworkLatencyMs,
                          @NetworkType,
                          CASE WHEN @NetworkType = 'WiFi' THEN @WifiSignalDbm ELSE NULL END,
                          CASE WHEN @NetworkType = 'LAN' THEN NULLIF(@LanLinkSpeed, '') ELSE NULL END,
                          @AdJoined, @TrellixInstalled, @DesktopCentralInstalled,
                          NULLIF(@LoggedInUser, ''), NULLIF(@LanIp, ''), NULLIF(@PublicIp, '') , NULLIF(@MacAddress, ''),
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
                              THEN CURRENT_TIMESTAMP
                              ELSE NULL
                          END,
                          CURRENT_TIMESTAMP)
                  ON CONFLICT (DeviceName) DO UPDATE SET
                      LastSiteName = CASE WHEN EXCLUDED.IsInternal THEN COALESCE(NULLIF(EXCLUDED.LastSiteName, ''), public.DeviceHeartbeats.LastSiteName) ELSE NULL END,
                      LastRegion = CASE WHEN EXCLUDED.IsInternal AND EXCLUDED.LastRegion IS NOT NULL THEN EXCLUDED.LastRegion ELSE CASE WHEN EXCLUDED.IsInternal THEN public.DeviceHeartbeats.LastRegion ELSE NULL END END,
                      LastInternalSeenUtc = CASE WHEN EXCLUDED.IsInternal AND EXCLUDED.LastRegion IS NOT NULL THEN CURRENT_TIMESTAMP ELSE CASE WHEN EXCLUDED.IsInternal THEN public.DeviceHeartbeats.LastInternalSeenUtc ELSE NULL END END,
                      IsInternal = EXCLUDED.IsInternal,
                      AppVersion = EXCLUDED.AppVersion,
                      CpuPercent = EXCLUDED.CpuPercent,
                      RamPercent = EXCLUDED.RamPercent,
                      DiskPercent = EXCLUDED.DiskPercent,
                      NetworkLatencyMs = EXCLUDED.NetworkLatencyMs,
                      NetworkType = EXCLUDED.NetworkType,
                      WifiSignalDbm = EXCLUDED.WifiSignalDbm,
                      LanLinkSpeed = EXCLUDED.LanLinkSpeed,
                      AdJoined = COALESCE(EXCLUDED.AdJoined, public.DeviceHeartbeats.AdJoined),
                      TrellixInstalled = COALESCE(EXCLUDED.TrellixInstalled, public.DeviceHeartbeats.TrellixInstalled),
                      DesktopCentralInstalled = COALESCE(EXCLUDED.DesktopCentralInstalled, public.DeviceHeartbeats.DesktopCentralInstalled),
                      LoggedInUser = COALESCE(NULLIF(EXCLUDED.LoggedInUser, ''), public.DeviceHeartbeats.LoggedInUser),
                      LanIp = COALESCE(NULLIF(EXCLUDED.LanIp, ''), public.DeviceHeartbeats.LanIp),
                      PublicIp = COALESCE(NULLIF(EXCLUDED.PublicIp, ''), public.DeviceHeartbeats.PublicIp),
                      MacAddress = COALESCE(NULLIF(EXCLUDED.MacAddress, ''), public.DeviceHeartbeats.MacAddress),
                      Domain = COALESCE(NULLIF(EXCLUDED.Domain, ''), public.DeviceHeartbeats.Domain),
                      WindowsVersion = COALESCE(NULLIF(EXCLUDED.WindowsVersion, ''), public.DeviceHeartbeats.WindowsVersion),
                      SerialNumber = COALESCE(NULLIF(EXCLUDED.SerialNumber, ''), public.DeviceHeartbeats.SerialNumber),
                      CpuModel = COALESCE(NULLIF(EXCLUDED.CpuModel, ''), public.DeviceHeartbeats.CpuModel),
                      RamTotal = COALESCE(NULLIF(EXCLUDED.RamTotal, ''), public.DeviceHeartbeats.RamTotal),
                      DiskTotal = COALESCE(NULLIF(EXCLUDED.DiskTotal, ''), public.DeviceHeartbeats.DiskTotal),
                      Mainboard = COALESCE(NULLIF(EXCLUDED.Mainboard, ''), public.DeviceHeartbeats.Mainboard),
                      Uptime = COALESCE(NULLIF(EXCLUDED.Uptime, ''), public.DeviceHeartbeats.Uptime),
                      DetailUpdatedUtc = COALESCE(EXCLUDED.DetailUpdatedUtc, public.DeviceHeartbeats.DetailUpdatedUtc),
                      LastSeenUtc = CURRENT_TIMESTAMP;",
            new
            {
                DeviceName = effectiveDeviceName,
                SiteName = effectiveSiteName,
                Region = effectiveRegion,
                IsInternal = effectiveIsInternal,
                HasNetworkData = !string.IsNullOrWhiteSpace(dto.LanIp) || !string.IsNullOrWhiteSpace(dto.PublicIp),
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

    private async Task UpdateDeviceHistoryAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid deviceId, HeartbeatDto dto)
    {
        var current = await conn.QuerySingleOrDefaultAsync<dynamic>(
                        @"SELECT ComputerName, CurrentUser AS Username, CurrentDepartment AS Department, CurrentLocation AS Location, MACAddress
                            FROM public.Devices
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
                        @"INSERT INTO public.DeviceHistory (DeviceId, MACAddress, ComputerName, Username, Department, Location, Timestamp, ChangeType)
                            VALUES (@DeviceId, @MacAddress, @ComputerName, @Username, @Department, @Location, CURRENT_TIMESTAMP, 'DEVICE_INFO_CHANGED')",
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
        await conn.OpenAsync();
        var deviceId = ResolveDeviceId(dto.DeviceId, dto.DeviceName, dto.MacAddress);
        var site = await ResolveSiteAsync(conn, null, dto.IPAddress);
        var isInternal = site is not null;

        await conn.ExecuteAsync(
                        @"INSERT INTO public.NetworkStatus (DeviceId, ConnectionType, AdapterName, IPAddress, MACAddress, SSID, SignalStrengthDbm, LinkSpeedMbps, DownloadMbps, UploadMbps, Timestamp)
                            VALUES (@DeviceId, @ConnectionType, @AdapterName, @IPAddress, @MacAddress, @SSID, @SignalStrengthDbm, @LinkSpeedMbps, @DownloadMbps, @UploadMbps, CURRENT_TIMESTAMP)",
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

        await conn.ExecuteAsync(
            @"UPDATE public.DeviceHeartbeats
              SET LastSiteName = CASE WHEN @IsInternal THEN @SiteName ELSE NULL END,
                  LastRegion = CASE WHEN @IsInternal THEN @Region ELSE NULL END,
                  LastInternalSeenUtc = CASE WHEN @IsInternal THEN CURRENT_TIMESTAMP ELSE NULL END,
                  IsInternal = @IsInternal,
                  NetworkType = @ConnectionType,
                  WifiSignalDbm = CASE WHEN @ConnectionType = 'WiFi' THEN @SignalStrengthDbm ELSE NULL END,
                  LanLinkSpeed = CASE WHEN @ConnectionType = 'LAN' THEN CAST(@LinkSpeedMbps AS text) ELSE NULL END,
                  LastSeenUtc = CURRENT_TIMESTAMP
              WHERE LOWER(DeviceName) = LOWER(@DeviceName);
              UPDATE public.Devices
              SET Status = CASE WHEN @IsInternal THEN 'Active' ELSE 'External' END,
                  LastSeen = CURRENT_TIMESTAMP
              WHERE LOWER(ComputerName) = LOWER(@DeviceName);",
            new
            {
                DeviceName = dto.DeviceName,
                IsInternal = isInternal,
                SiteName = site?.Site,
                Region = site?.Region,
                ConnectionType = dto.ConnectionType,
                SignalStrengthDbm = dto.SignalStrengthDbm,
                LinkSpeedMbps = dto.LinkSpeedMbps
            });
    }

    public async Task TouchAgentActivityAsync(string deviceName)
    {
        using var conn = CreateConnection();
        await TouchAgentActivityAsync(conn, deviceName);
    }

    private static async Task TouchAgentActivityAsync(NpgsqlConnection conn, string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return;

        await conn.ExecuteAsync(
            @"UPDATE public.DeviceHeartbeats
              SET LastSeenUtc = CURRENT_TIMESTAMP
              WHERE LOWER(DeviceName) = LOWER(@DeviceName);
              UPDATE public.Devices
              SET LastSeen = CURRENT_TIMESTAMP
              WHERE LOWER(ComputerName) = LOWER(@DeviceName);",
            new { DeviceName = deviceName });
    }

    public async Task RecordPerformanceLogAsync(PerformanceLogDto dto)
    {
        using var conn = CreateConnection();
        var deviceId = ResolveDeviceId(dto.DeviceId, dto.DeviceName, null);

        await conn.ExecuteAsync(
                        @"INSERT INTO public.PerformanceLogs (DeviceId, CPUUsage, RAMUsage, DiskUsage, DiskRead, DiskWrite, DiskIO, TopProcess, Timestamp)
                            VALUES (@DeviceId, @CpuUsage, @RamUsage, @DiskUsage, @DiskRead, @DiskWrite, @DiskIO, @TopProcess, CURRENT_TIMESTAMP)",
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

            await TouchAgentActivityAsync(conn, dto.DeviceName);
    }

    public async Task RecordComplianceStatusAsync(ComplianceStatusDto dto)
    {
        using var conn = CreateConnection();
        var deviceId = ResolveDeviceId(dto.DeviceId, dto.DeviceName, null);

        await conn.ExecuteAsync(
                        @"INSERT INTO public.ComplianceStatus (DeviceId, Antivirus, Firewall, WindowsUpdate, BitLocker, PasswordPolicy, EndpointProtection, OverallStatus, Timestamp)
                            VALUES (@DeviceId, @Antivirus, @Firewall, @WindowsUpdate, @BitLocker, @PasswordPolicy, @EndpointProtection, @OverallStatus, CURRENT_TIMESTAMP)",
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

            await TouchAgentActivityAsync(conn, dto.DeviceName);
    }

    public async Task UpsertSoftwareInventoryAsync(SoftwareInventoryEntryDto dto)
    {
        using var conn = CreateConnection();

        // Try to find existing device by ComputerName (case-insensitive) using the Devices table.
        var existingDeviceId = await conn.ExecuteScalarAsync<Guid?>(
            @"SELECT DeviceId FROM public.Devices WHERE LOWER(ComputerName) = LOWER(@DeviceName) ORDER BY LastSeen DESC LIMIT 1",
            new { DeviceName = dto.DeviceName });

        var deviceId = existingDeviceId ?? ResolveDeviceId(dto.DeviceId, dto.DeviceName, null);

        await conn.ExecuteAsync(
                        @"INSERT INTO public.SoftwareInventory (DeviceId, SoftwareName, Version, Publisher, InstallDate, FirstDetected, LastDetected)
                            VALUES (@DeviceId, @SoftwareName, NULLIF(@Version, ''), NULLIF(@Publisher, ''), @InstallDate, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                            ON CONFLICT (DeviceId, SoftwareName) DO UPDATE SET Version = COALESCE(NULLIF(EXCLUDED.Version, ''), public.SoftwareInventory.Version), Publisher = COALESCE(NULLIF(EXCLUDED.Publisher, ''), public.SoftwareInventory.Publisher), InstallDate = COALESCE(EXCLUDED.InstallDate, public.SoftwareInventory.InstallDate), LastDetected = CURRENT_TIMESTAMP;",
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
                    @"SELECT DeviceId FROM public.Devices WHERE LOWER(ComputerName) = LOWER(@DeviceName) ORDER BY LastSeen DESC LIMIT 1",
                    new { DeviceName = deviceName }, transaction: tx);

                Guid deviceId = existingDeviceId ?? ResolveDeviceId(null, deviceName, null);

                foreach (var item in grp)
                {
                    await conn.ExecuteAsync(
                                                @"INSERT INTO public.SoftwareInventory (DeviceId, SoftwareName, Version, Publisher, InstallDate, FirstDetected, LastDetected)
                                                    VALUES (@DeviceId, @SoftwareName, NULLIF(@Version, ''), NULLIF(@Publisher, ''), @InstallDate, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                                                    ON CONFLICT (DeviceId, SoftwareName) DO UPDATE SET Version = COALESCE(NULLIF(EXCLUDED.Version, ''), public.SoftwareInventory.Version), Publisher = COALESCE(NULLIF(EXCLUDED.Publisher, ''), public.SoftwareInventory.Publisher), InstallDate = COALESCE(EXCLUDED.InstallDate, public.SoftwareInventory.InstallDate), LastDetected = CURRENT_TIMESTAMP;",
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
            @"UPDATE public.Devices
              SET NetworkWarningDisabled = @DisableWarning,
                  NetworkWarningDisabledAt = CASE WHEN @DisableWarning THEN CURRENT_TIMESTAMP ELSE NULL END
              WHERE DeviceId = @DeviceId",
            new { DeviceId = deviceId, DisableWarning = disableWarning });
    }

    public async Task<AgentStatusDto?> GetAgentStatusAsync(string? deviceNameOrId, decimal cpuWarningPercent, decimal ramWarningPercent, decimal diskWarningPercent, decimal diskCriticalPercent, int diskIoWarning)
    {
        using var conn = CreateConnection();

        Guid? parsedDeviceId = null;
        if (Guid.TryParse(deviceNameOrId, out var deviceId)) parsedDeviceId = deviceId;

        var device = await conn.QuerySingleOrDefaultAsync<(Guid DeviceId, string DeviceName, bool? NetworkWarningDisabled, string? Status, string? CurrentUser)>(
                        @"SELECT DeviceId, ComputerName AS DeviceName, NetworkWarningDisabled, Status, CurrentUser
              FROM public.Devices
              WHERE (@DeviceId IS NOT NULL AND DeviceId = @DeviceId)
                 OR (@DeviceName IS NOT NULL AND ComputerName = @DeviceName)
                            ORDER BY LastSeen DESC
                            LIMIT 1",
            new { DeviceId = parsedDeviceId, DeviceName = deviceNameOrId });

        if (device == default) return null;

        var network = await conn.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT ConnectionType, SSID, SignalStrengthDbm
              FROM public.NetworkStatus
              WHERE DeviceId = @DeviceId
              ORDER BY Timestamp DESC",
            new { DeviceId = device.DeviceId });

        var performance = await conn.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT CPUUsage, RAMUsage, DiskUsage, DiskIO
              FROM public.PerformanceLogs
              WHERE DeviceId = @DeviceId
              ORDER BY Timestamp DESC",
            new { DeviceId = device.DeviceId });

        var complianceStatus = await conn.QuerySingleOrDefaultAsync<string?>(
                @"SELECT OverallStatus
              FROM public.ComplianceStatus
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
                   FROM public.DeviceHeartbeats h
              ),
              LatestDevice AS (
                   SELECT d.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                   FROM public.Devices d
              )
              SELECT h.DeviceName,
                     d.DeviceId,
                     d.MACAddress,
                     d.CurrentUser,
                     d.CurrentDepartment,
                     d.CurrentLocation,
                     d.NetworkWarningDisabled,
                     CASE WHEN h.IsInternal THEN h.LastSiteName ELSE NULL END AS LastSiteName,
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
                    CASE WHEN h.LastSeenUtc >= CURRENT_TIMESTAMP - INTERVAL '5 minutes' THEN TRUE ELSE FALSE END AS IsOnline,
                     (SELECT COUNT(*) FROM public.PerformanceWarnings w WHERE w.DeviceName = h.DeviceName AND w.WarnedAtUtc >= CURRENT_DATE) AS WarningsToday,
                     c.OverallStatus AS ComplianceStatus,
                     CASE WHEN COALESCE(h.IsInternal, FALSE) THEN 'Internal' ELSE 'External' END AS ExternalNetworkStatus
              FROM LatestHeartbeat h
              LEFT JOIN LatestDevice d ON LOWER(d.ComputerName) = LOWER(h.DeviceName) AND d.RowNum = 1
              LEFT JOIN (
                  SELECT DeviceId, OverallStatus
                  FROM (
                      SELECT DeviceId, OverallStatus, ROW_NUMBER() OVER (PARTITION BY DeviceId ORDER BY Timestamp DESC) AS RowNum
                      FROM public.ComplianceStatus
                  ) x
                  WHERE RowNum = 1
              ) c ON c.DeviceId = d.DeviceId
              WHERE h.RowNum = 1
              ORDER BY h.LastSeenUtc DESC");
    }

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync()
    {
        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<(string? Region, bool IsInternal, bool? AdJoined, bool? TrellixInstalled, bool? DesktopCentralInstalled)>(
            @"WITH LatestHeartbeat AS (
                   SELECT h.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(h.DeviceName) ORDER BY h.LastSeenUtc DESC) AS RowNum
                   FROM public.DeviceHeartbeats h
              )
              SELECT LastRegion AS Region, IsInternal, AdJoined, TrellixInstalled, DesktopCentralInstalled
              FROM LatestHeartbeat
              WHERE RowNum = 1");

        var deviceRows = rows.ToList();
        var summary = new DashboardSummaryDto
        {
            TotalComputers = deviceRows.Count,
            Compliant = deviceRows.Count(row => row.AdJoined == true && row.TrellixInstalled == true && row.DesktopCentralInstalled == true),
            InternalNetwork = deviceRows.Count(row => row.IsInternal),
            ExternalNetwork = deviceRows.Count(row => !row.IsInternal)
        };

        summary.NonCompliant = summary.TotalComputers - summary.Compliant;
        foreach (var region in new[] { "VMB", "VMT", "VMN" })
        {
            summary.Regions[region] = deviceRows.Count(row => string.Equals(row.Region, region, StringComparison.OrdinalIgnoreCase));
        }

        return summary;
    }

    public async Task<IEnumerable<DeviceReportWarningDto>> GetTodayWarningsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<DeviceReportWarningDto>(
            @"SELECT DeviceName, MetricType, MetricValue, WarnedAtUtc, SiteName, Region
              FROM public.PerformanceWarnings
              WHERE WarnedAtUtc >= CURRENT_DATE
              ORDER BY WarnedAtUtc DESC");
    }

    // ---------------- Weekly report (dùng lại trong WeeklyReportJob) ----------------

    public async Task<IEnumerable<dynamic>> GetWeeklyReportAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync("SELECT * FROM public.vw_WeeklyPerformanceReport ORDER BY TotalWarningCount DESC");
    }

    public async Task<DeviceReportDataDto> GetDeviceReportAsync(string deviceName)
    {
        using var conn = CreateConnection();

        var device = await conn.QuerySingleOrDefaultAsync<DeviceDashboardDto>(
            @"WITH LatestHeartbeat AS (
                   SELECT h.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(h.DeviceName) ORDER BY h.LastSeenUtc DESC, h.DeviceName) AS RowNum
                   FROM public.DeviceHeartbeats h
                   WHERE LOWER(h.DeviceName) = LOWER(@DeviceName)
               ),
               LatestDevice AS (
                   SELECT d.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                   FROM public.Devices d
                   WHERE LOWER(d.ComputerName) = LOWER(@DeviceName)
               )
               SELECT h.DeviceName,
                     d.DeviceId,
                     d.MACAddress,
                     d.CurrentUser,
                     d.CurrentDepartment,
                     d.CurrentLocation,
                     d.NetworkWarningDisabled,
                     CASE WHEN h.IsInternal THEN h.LastSiteName ELSE NULL END AS LastSiteName,
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
                    h.LastSeenUtc >= CURRENT_TIMESTAMP - INTERVAL '5 minutes' AS IsOnline,
                     (SELECT COUNT(*) FROM public.PerformanceWarnings w WHERE LOWER(w.DeviceName) = LOWER(@DeviceName) AND w.WarnedAtUtc >= CURRENT_DATE) AS WarningsToday,
                     c.OverallStatus AS ComplianceStatus,
                     CASE WHEN COALESCE(h.IsInternal, FALSE) THEN 'Internal' ELSE 'External' END AS ExternalNetworkStatus
              FROM LatestHeartbeat h
              LEFT JOIN LatestDevice d ON LOWER(d.ComputerName) = LOWER(h.DeviceName) AND d.RowNum = 1
              LEFT JOIN (
                  SELECT DeviceId, OverallStatus
                  FROM (
                      SELECT DeviceId, OverallStatus, ROW_NUMBER() OVER (PARTITION BY DeviceId ORDER BY Timestamp DESC) AS RowNum
                      FROM public.ComplianceStatus
                  ) x
                  WHERE RowNum = 1
              ) c ON c.DeviceId = d.DeviceId
              WHERE h.RowNum = 1
              ORDER BY h.LastSeenUtc DESC
              LIMIT 1",
            new { DeviceName = deviceName });

        var warnings = (await conn.QueryAsync<DeviceReportWarningDto>(
            @"SELECT DeviceName, MetricType, MetricValue, WarnedAtUtc, SiteName, Region
              FROM public.PerformanceWarnings
              WHERE LOWER(DeviceName) = LOWER(@DeviceName)
                AND WarnedAtUtc >= CURRENT_TIMESTAMP - INTERVAL '7 days'
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
                   FROM public.DeviceHeartbeats h
                   WHERE LOWER(h.DeviceName) = ANY(@DeviceNames)
               ),
               LatestDevice AS (
                   SELECT d.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                   FROM public.Devices d
               )
               SELECT h.DeviceName,
                     d.DeviceId,
                     d.MACAddress,
                     d.CurrentUser,
                     d.CurrentDepartment,
                     d.CurrentLocation,
                     d.NetworkWarningDisabled,
                     CASE WHEN h.IsInternal THEN h.LastSiteName ELSE NULL END AS LastSiteName,
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
                     h.LastSeenUtc >= CURRENT_TIMESTAMP - INTERVAL '5 minutes' AS IsOnline,
                     (SELECT COUNT(*) FROM public.PerformanceWarnings w WHERE w.DeviceName = h.DeviceName AND w.WarnedAtUtc >= CURRENT_DATE) AS WarningsToday,
                     c.OverallStatus AS ComplianceStatus,
                     CASE WHEN COALESCE(h.IsInternal, FALSE) THEN 'Internal' ELSE 'External' END AS ExternalNetworkStatus
               FROM LatestHeartbeat h
               LEFT JOIN LatestDevice d ON LOWER(d.ComputerName) = LOWER(h.DeviceName) AND d.RowNum = 1
               LEFT JOIN (
                  SELECT DeviceId, OverallStatus
                  FROM (
                      SELECT DeviceId, OverallStatus, ROW_NUMBER() OVER (PARTITION BY DeviceId ORDER BY Timestamp DESC) AS RowNum
                      FROM public.ComplianceStatus
                  ) x
                  WHERE RowNum = 1
               ) c ON c.DeviceId = d.DeviceId
               WHERE h.RowNum = 1
               ORDER BY h.LastSeenUtc DESC"
            : @"WITH LatestHeartbeat AS (
                   SELECT h.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(h.DeviceName) ORDER BY h.LastSeenUtc DESC, h.DeviceName) AS RowNum
                   FROM public.DeviceHeartbeats h
               ),
               LatestDevice AS (
                   SELECT d.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                   FROM public.Devices d
               )
               SELECT h.DeviceName,
                     d.DeviceId,
                     d.MACAddress,
                     d.CurrentUser,
                     d.CurrentDepartment,
                     d.CurrentLocation,
                     d.NetworkWarningDisabled,
                     CASE WHEN h.IsInternal THEN h.LastSiteName ELSE NULL END AS LastSiteName,
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
                     h.LastSeenUtc >= CURRENT_TIMESTAMP - INTERVAL '5 minutes' AS IsOnline,
                     (SELECT COUNT(*) FROM public.PerformanceWarnings w WHERE w.DeviceName = h.DeviceName AND w.WarnedAtUtc >= CURRENT_DATE) AS WarningsToday,
                     c.OverallStatus AS ComplianceStatus,
                     CASE WHEN COALESCE(h.IsInternal, FALSE) THEN 'Internal' ELSE 'External' END AS ExternalNetworkStatus
               FROM LatestHeartbeat h
               LEFT JOIN LatestDevice d ON LOWER(d.ComputerName) = LOWER(h.DeviceName) AND d.RowNum = 1
               LEFT JOIN (
                  SELECT DeviceId, OverallStatus
                  FROM (
                      SELECT DeviceId, OverallStatus, ROW_NUMBER() OVER (PARTITION BY DeviceId ORDER BY Timestamp DESC) AS RowNum
                      FROM public.ComplianceStatus
                  ) x
                  WHERE RowNum = 1
               ) c ON c.DeviceId = d.DeviceId
               WHERE h.RowNum = 1
               ORDER BY h.LastSeenUtc DESC";

        var warningsSql = hasFilter
            ? @"SELECT DeviceName, MetricType, MetricValue, WarnedAtUtc, SiteName, Region
                FROM public.PerformanceWarnings
                WHERE LOWER(DeviceName) = ANY(@DeviceNames)
                  AND WarnedAtUtc >= CURRENT_TIMESTAMP - INTERVAL '7 days'
                ORDER BY DeviceName, WarnedAtUtc DESC"
            : @"SELECT DeviceName, MetricType, MetricValue, WarnedAtUtc, SiteName, Region
                FROM public.PerformanceWarnings
                WHERE WarnedAtUtc >= CURRENT_TIMESTAMP - INTERVAL '7 days'
                ORDER BY DeviceName, WarnedAtUtc DESC";

        var normalizedDeviceNames = deviceNames?.Select(name => name.ToLowerInvariant()).ToArray();
        var devices = (await conn.QueryAsync<DeviceDashboardDto>(devicesSql, new { DeviceNames = normalizedDeviceNames })).ToList();
        var warnings = (await conn.QueryAsync<DeviceReportWarningDto>(warningsSql, new { DeviceNames = normalizedDeviceNames })).ToList();

        return (devices, warnings);
    }

    public async Task<DeviceDetailDto?> GetDeviceDetailAsync(string deviceName)
    {
        using var conn = CreateConnection();
        var device = await conn.QuerySingleOrDefaultAsync<DeviceDetailDto>(
            @"WITH LatestHeartbeat AS (
                   SELECT h.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(h.DeviceName) ORDER BY h.LastSeenUtc DESC, h.DeviceName) AS RowNum
                   FROM public.DeviceHeartbeats h
               ),
               LatestDevice AS (
                   SELECT d.*,
                          ROW_NUMBER() OVER (PARTITION BY LOWER(d.ComputerName) ORDER BY d.LastSeen DESC, d.DeviceId DESC) AS RowNum
                   FROM public.Devices d
               )
               SELECT
                   d.ComputerName AS DeviceName,
                   d.DeviceId,
                   d.CurrentUser,
                   d.CurrentDepartment,
                   d.CurrentLocation,
                   h.LoggedInUser,
                   h.LanIp,
                   h.PublicIp,
                   d.MACAddress,
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
                   d.Status AS CurrentStatus,
                   d.NetworkWarningDisabled,
                   h.NetworkType,
                   h.WifiSignalDbm,
                   h.LanLinkSpeed,
                   CASE WHEN COALESCE(h.IsInternal, FALSE) THEN 'Internal' ELSE 'External' END AS ExternalNetworkStatus,
                   COALESCE(h.LastSeenUtc, d.LastSeen) >= CURRENT_TIMESTAMP - INTERVAL '5 minutes' AS IsOnline
               FROM LatestDevice d
               LEFT JOIN LatestHeartbeat h ON LOWER(h.DeviceName) = LOWER(d.ComputerName) AND h.RowNum = 1
               WHERE LOWER(d.ComputerName) = LOWER(@DeviceName)
                 AND d.RowNum = 1
               ORDER BY COALESCE(h.LastSeenUtc, d.LastSeen) DESC
               LIMIT 1",
            new { DeviceName = deviceName });

        if (device == null) return null;

        var complianceStatus = await conn.QuerySingleOrDefaultAsync<string?>(
                        @"SELECT OverallStatus
                            FROM public.ComplianceStatus
              WHERE DeviceId = @DeviceId
                            ORDER BY Timestamp DESC
                            LIMIT 1",
            new { DeviceId = device.DeviceId });

        device.ComplianceStatus = complianceStatus;
        return device;
    }

    public async Task<List<SoftwareInventoryEntryDto>> GetSoftwareInventoryAsync(string deviceName)
    {
        using var conn = CreateConnection();

        var deviceId = await conn.ExecuteScalarAsync<Guid?>(
            @"SELECT DeviceId
              FROM public.Devices
              WHERE LOWER(ComputerName) = LOWER(@DeviceName)
              ORDER BY LastSeen DESC
              LIMIT 1",
            new { DeviceName = deviceName });

        if (deviceId is null)
            return new List<SoftwareInventoryEntryDto>();

        return (await conn.QueryAsync<SoftwareInventoryEntryDto>(
            @"SELECT s.SoftwareName, s.Version, s.Publisher, s.InstallDate,
                     d.ComputerName AS DeviceName,
                     CAST(s.DeviceId AS VARCHAR(36)) AS DeviceId
              FROM public.SoftwareInventory s
              INNER JOIN public.Devices d ON d.DeviceId = s.DeviceId
              WHERE s.DeviceId = @DeviceId
              ORDER BY s.SoftwareName",
            new { DeviceId = deviceId })).ToList();
    }
}
