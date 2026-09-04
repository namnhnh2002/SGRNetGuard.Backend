namespace SGRNetGuard.Api.Models;

public class SiteDto
{
    public string Region { get; set; } = "";
    public string Site { get; set; } = "";
    public string Subnet { get; set; } = "";
    public string IT { get; set; } = "";
    public string Teams { get; set; } = "";
}

/// <summary>
/// Trả về cho client - thay thế hoàn toàn nội dung config.json cũ.
/// ConfigVersion dùng để client so sánh, nếu không đổi thì khỏi cần tải lại toàn bộ.
/// </summary>
public class RemoteConfigDto
{
    public string ConfigVersion { get; set; } = "";
    public string Ssid { get; set; } = "";
    public Dictionary<string, List<string>> DnsServersByRegion { get; set; } = new();
    public List<SiteDto> Sites { get; set; } = new();
}

/// <summary>
/// Client gửi lên mỗi khi Popup "Cảnh báo hiệu năng hệ thống" hiển thị.
/// </summary>
public class PerformanceWarningDto
{
    public string DeviceName { get; set; } = "";
    public string? SiteName { get; set; }
    public string? Region { get; set; }
    public string MetricType { get; set; } = ""; // CPU / RAM / Disk
    public decimal MetricValue { get; set; }
}

public class HeartbeatDto
{
    public string DeviceName { get; set; } = "";
    public string? DeviceId { get; set; }
    public string? ComputerName { get; set; }
    public string? Username { get; set; }
    public string? Department { get; set; }
    public string? Location { get; set; }
    public string? OperatingSystem { get; set; }
    public string? SiteName { get; set; }
    public string? Region { get; set; }
    public bool IsInternal { get; set; }
    public string? AppVersion { get; set; }
    public string? MacAddress { get; set; }
    public string? NetworkType { get; set; }
    public int? WifiSignalDbm { get; set; }
    public string? LanLinkSpeed { get; set; }

    // Chỉ số hiệu năng live - hiển thị trên Dashboard IT
    public decimal? CpuPercent { get; set; }
    public decimal? RamPercent { get; set; }
    public decimal? DiskPercent { get; set; }
    public int? NetworkLatencyMs { get; set; }

    // Tuân thủ ANBM
    public bool? AdJoined { get; set; }
    public bool? TrellixInstalled { get; set; }
    public bool? DesktopCentralInstalled { get; set; }

    // Chi tiết máy cho trang device detail
    public string? LoggedInUser { get; set; }
    public string? LanIp { get; set; }
    public string? PublicIp { get; set; }
    public string? Domain { get; set; }
    public string? WindowsVersion { get; set; }
    public string? SerialNumber { get; set; }
    public string? CpuModel { get; set; }
    public string? RamTotal { get; set; }
    public string? DiskTotal { get; set; }
    public string? Mainboard { get; set; }
    public string? Uptime { get; set; }
}

public class DeviceDashboardDto
{
    public string DeviceName { get; set; } = "";
    public Guid? DeviceId { get; set; }
    public string? MacAddress { get; set; }
    public string? CurrentUser { get; set; }
    public string? CurrentDepartment { get; set; }
    public string? CurrentLocation { get; set; }
    public bool? NetworkWarningDisabled { get; set; }
    public string? LastSiteName { get; set; }
    public string? LastRegion { get; set; }
    public bool IsInternal { get; set; }
    public decimal? CpuPercent { get; set; }
    public decimal? RamPercent { get; set; }
    public decimal? DiskPercent { get; set; }
    public int? NetworkLatencyMs { get; set; }
    public bool? AdJoined { get; set; }
    public bool? TrellixInstalled { get; set; }
    public bool? DesktopCentralInstalled { get; set; }
    public string? AppVersion { get; set; }
    public string? NetworkType { get; set; }
    public int? WifiSignalDbm { get; set; }
    public string? LanLinkSpeed { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public bool IsOnline { get; set; }
    public int WarningsToday { get; set; }
    public string? ComplianceStatus { get; set; }
    public string? ExternalNetworkStatus { get; set; }
}

public class DeviceReportWarningDto
{
    public string DeviceName { get; set; } = "";
    public string MetricType { get; set; } = "";
    public decimal MetricValue { get; set; }
    public DateTime WarnedAtUtc { get; set; }
    public string? SiteName { get; set; }
    public string? Region { get; set; }
}

public class DeviceReportDataDto
{
    public DeviceDashboardDto? Device { get; set; }
    public List<DeviceReportWarningDto> Warnings { get; set; } = new();
}

public class DeviceBulkReportRequestDto
{
    public List<string> DeviceNames { get; set; } = new();
}

public class DeviceDetailDto
{
    public string DeviceName { get; set; } = "";
    public Guid? DeviceId { get; set; }
    public string? LoggedInUser { get; set; }
    public string? LanIp { get; set; }
    public string? PublicIp { get; set; }
    public string? MacAddress { get; set; }
    public string? Domain { get; set; }
    public string? WindowsVersion { get; set; }
    public string? SerialNumber { get; set; }
    public string? CpuModel { get; set; }
    public string? RamTotal { get; set; }
    public string? DiskTotal { get; set; }
    public string? Mainboard { get; set; }
    public string? Uptime { get; set; }
    public string? AppVersion { get; set; }
    public DateTime? DetailUpdatedUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public bool IsOnline { get; set; }
    public string? CurrentUser { get; set; }
    public string? CurrentDepartment { get; set; }
    public string? CurrentLocation { get; set; }
    public string? CurrentStatus { get; set; }
    public bool? NetworkWarningDisabled { get; set; }
    public string? ComplianceStatus { get; set; }
    public string? ExternalNetworkStatus { get; set; }
    public string? NetworkType { get; set; }
    public int? WifiSignalDbm { get; set; }
    public string? LanLinkSpeed { get; set; }
}

public class NetworkStatusDto
{
    public string DeviceName { get; set; } = "";
    public string? DeviceId { get; set; }
    public string? ConnectionType { get; set; }
    public string? AdapterName { get; set; }
    public string? IPAddress { get; set; }
    public string? MacAddress { get; set; }
    public string? SSID { get; set; }
    public double? SignalStrengthDbm { get; set; }
    public double? LinkSpeedMbps { get; set; }
    public double? DownloadMbps { get; set; }
    public double? UploadMbps { get; set; }
}

public class PerformanceLogDto
{
    public string DeviceName { get; set; } = "";
    public string? DeviceId { get; set; }
    public decimal? CpuUsage { get; set; }
    public decimal? RamUsage { get; set; }
    public decimal? DiskUsage { get; set; }
    public decimal? DiskRead { get; set; }
    public decimal? DiskWrite { get; set; }
    public decimal? DiskIO { get; set; }
    public string? TopProcess { get; set; }
}

public class ComplianceStatusDto
{
    public string DeviceName { get; set; } = "";
    public string? DeviceId { get; set; }
    public string? Antivirus { get; set; }
    public string? Firewall { get; set; }
    public string? WindowsUpdate { get; set; }
    public string? BitLocker { get; set; }
    public string? PasswordPolicy { get; set; }
    public string? EndpointProtection { get; set; }
    public string? OverallStatus { get; set; }
}

public class SoftwareInventoryEntryDto
{
    public string DeviceName { get; set; } = "";
    public string? DeviceId { get; set; }
    public string SoftwareName { get; set; } = "";
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public DateTime? InstallDate { get; set; }
}

public class DevicePreferenceRequestDto
{
    public string? DeviceName { get; set; }
    public string? DeviceId { get; set; }
    public string Action { get; set; } = "dismiss";
}

public class SystemSettingsDto
{
    public string SystemName { get; set; } = "SGR NetGuard";
    public string CompanyName { get; set; } = "Sun Group";
    public string ApiServerUrl { get; set; } = "http://127.0.0.1:5080";
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";
    public string Language { get; set; } = "vi";
    public bool RealtimeEnabled { get; set; } = true;
    public string DashboardUsername { get; set; } = "admin";
    public string DashboardPassword { get; set; } = "Sun@2026";
}

public class AgentStatusDto
{
    public string DeviceName { get; set; } = "";
    public Guid? DeviceId { get; set; }
    public bool IsOk { get; set; }
    public bool IsExpanded { get; set; }
    public bool NetworkWarningDisabled { get; set; }
    public bool HasExternalNetworkWarning { get; set; }
    public bool HasSecurityWarning { get; set; }
    public bool HasPerformanceWarning { get; set; }
    public string Summary { get; set; } = "";
    public string? NetworkWarningMessage { get; set; }
    public string? SecurityWarningMessage { get; set; }
    public string? PerformanceWarningMessage { get; set; }
    public string? ComplianceStatus { get; set; }
    public string? ConnectionType { get; set; }
    public string? Ssid { get; set; }
    public double? SignalStrengthDbm { get; set; }
    public string? CurrentUser { get; set; }
    public bool IsInternal { get; set; }
}
