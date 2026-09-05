// ============================================================================
// SGR NetGuard - CLIENT INTEGRATION SNIPPETS
// Đây là các đoạn code CẦN CHÈN THÊM vào project app hiện tại của bạn
// (KHÔNG PHẢI project độc lập - copy từng phần vào đúng chỗ tương ứng)
// ============================================================================

// ----------------------------------------------------------------------------
// 1. THÊM MỚI: Services/RemoteConfigClient.cs
//    Thay thế / bổ sung cho ConfigManager cũ - tải config từ API thay vì chỉ đọc file local.
// ----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace SGRNetworkAgent.Services;

public class RemoteConfigDto
{
    public string ConfigVersion { get; set; } = "";
    public string Ssid { get; set; } = "";
    public Dictionary<string, List<string>> DnsServersByRegion { get; set; } = new();
    public List<SiteRemoteDto> Sites { get; set; } = new();
}

public class SiteRemoteDto
{
    public string Region { get; set; } = "";
    public string Site { get; set; } = "";
    public string Subnet { get; set; } = "";
    public string IT { get; set; } = "";
    public string Teams { get; set; } = "";
}

/// <summary>
/// Tải config từ API trung tâm (thay cho đọc config.json tĩnh).
/// - Luôn cache lại bản mới nhất vào local (%LocalAppData%\SGRNetworkAgent\config.cache.json)
///   để app vẫn hoạt động được nếu mất mạng / API tạm thời không truy cập được.
/// - Gọi 1 lần lúc khởi động + định kỳ (ví dụ mỗi 6 tiếng qua Timer, xem phần 3 bên dưới).
/// </summary>
public class RemoteConfigClient
{
    private static readonly string ApiBaseUrl =
        (Environment.GetEnvironmentVariable("SGR_NETGUARD_API_URL")?.TrimEnd('/'))
        is { Length: > 0 } configuredUrl
            ? configuredUrl
            : "https://sgrnetguard-backend.onrender.com";

    private readonly HttpClient _http;
    private readonly string _cacheFilePath;

    public RemoteConfigDto? Current { get; private set; }

    public RemoteConfigClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _cacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SGRNetworkAgent", "config.cache.json");
    }

    public async Task<bool> LoadAsync()
    {
        try
        {
            var response = await _http.GetFromJsonAsync<RemoteConfigDto>($"{ApiBaseUrl}/api/config");
            if (response == null) return false;

            Current = response;

            // Cache lại để dùng offline lần sau
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFilePath)!);
            File.WriteAllText(_cacheFilePath, JsonSerializer.Serialize(response));

            return true;
        }
        catch
        {
            // Không gọi được API (mất mạng, server down...) -> dùng bản cache cũ nếu có
            return TryLoadFromCache();
        }
    }

    private bool TryLoadFromCache()
    {
        try
        {
            if (!File.Exists(_cacheFilePath)) return false;
            var json = File.ReadAllText(_cacheFilePath);
            Current = JsonSerializer.Deserialize<RemoteConfigDto>(json);
            return Current != null;
        }
        catch
        {
            return false;
        }
    }
}


// ----------------------------------------------------------------------------
// 2. SỬA NetworkDetectionEngine.cs
//    Đổi phần lấy DNS hợp lệ từ "config.DnsServers" (danh sách chung)
//    sang lấy theo ĐÚNG VÙNG của site đang xét (config.DnsServersByRegion[site.Region]).
//    Đây cũng là chỗ khắc phục vấn đề "1 danh sách DNS dùng chung cho cả nước" đã nói trước đó.
// ----------------------------------------------------------------------------

/*
    Trong NetworkDetectionEngine.Detect(), đoạn so khớp DNS hiện tại:

        bool dnsValid = adapter.DnsServers.Any(dns =>
            config.DnsServers.Contains(dns, StringComparer.OrdinalIgnoreCase));

    Đổi thành so khớp theo vùng của site đang xét (đặt SAU khi đã tìm ra matchedSite):

        var regionDns = config.DnsServersByRegion.TryGetValue(matchedSite.Region, out var list)
            ? list : new List<string>();

        bool dnsValid = adapter.DnsServers.Any(dns =>
            regionDns.Contains(dns, StringComparer.OrdinalIgnoreCase));
*/


// ----------------------------------------------------------------------------
// 3. SỬA App.xaml.cs (hoặc file khởi tạo tương đương)
//    - Khởi tạo RemoteConfigClient thay vì / kèm ConfigManager cũ.
//    - Thêm Timer tải lại config mỗi 6 tiếng.
// ----------------------------------------------------------------------------

/*
    private RemoteConfigClient _remoteConfig = null!;
    private System.Timers.Timer _configRefreshTimer = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ...
        _remoteConfig = new RemoteConfigClient();
        await _remoteConfig.LoadAsync();   // tải lần đầu lúc khởi động

        _configRefreshTimer = new System.Timers.Timer(6 * 60 * 60 * 1000); // 6 tiếng
        _configRefreshTimer.Elapsed += async (_, _) => await _remoteConfig.LoadAsync();
        _configRefreshTimer.Start();
        ...
    }
*/


// ----------------------------------------------------------------------------
// 4. THÊM MỚI: Services/TelemetryClient.cs
//    Gửi sự kiện cảnh báo hiệu năng về server mỗi khi Popup "Cảnh báo hiệu năng
//    hệ thống" hiển thị (theo đúng ảnh chụp Popup màu đỏ bạn gửi trước đó).
// ----------------------------------------------------------------------------

namespace SGRNetworkAgent.Services;

public class TelemetryClient
{
    private static readonly string ApiBaseUrl =
        (Environment.GetEnvironmentVariable("SGR_NETGUARD_API_URL")?.TrimEnd('/'))
        is { Length: > 0 } configuredUrl
            ? configuredUrl
            : "https://sgrnetguard-backend.onrender.com";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>
    /// Gọi hàm này ngay tại chỗ code hiện tại của bạn đang hiển thị
    /// Popup "Cảnh báo hiệu năng hệ thống" (CPU/RAM/Disk cao liên tục > 5 phút).
    /// Lưu ý hành vi:
    /// - Nút "Đã hiểu" => gửi action = "dismiss" và lưu trạng thái không nhắc lại cho thiết bị.
    /// - Nút "Đóng" => gửi action = "close" và KHÔNG lưu trạng thái tắt cảnh báo vĩnh viễn.
    /// - Dashboard IT vẫn nhận cảnh báo bình thường.
    /// </summary>
    public async Task ReportWarningAsync(string metricType, decimal metricValue, string? siteName, string? region)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync($"{ApiBaseUrl}/api/telemetry/warning", new
            {
                DeviceName = Environment.MachineName,
                SiteName = siteName,
                Region = region,
                MetricType = metricType,   // "CPU" / "RAM" / "Disk"
                MetricValue = metricValue
            });
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            // Gửi telemetry thất bại (mất mạng...) - bỏ qua, không ảnh hưởng trải nghiệm user.
            // Có thể ghi vào Serilog nếu muốn theo dõi tỉ lệ gửi thất bại.
        }
    }

    public async Task SendHeartbeatAsync(
        string? siteName, string? region, bool isInternal,
        decimal cpuPercent, decimal ramPercent, decimal diskPercent, int networkLatencyMs,
        bool adJoined, bool trellixInstalled, bool desktopCentralInstalled)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync($"{ApiBaseUrl}/api/heartbeat", new
            {
                DeviceName = Environment.MachineName,
                ComputerName = Environment.MachineName,
                SiteName = siteName,
                Region = region,
                IsInternal = isInternal,
                AppVersion = typeof(TelemetryClient).Assembly.GetName().Version?.ToString(),
                CpuPercent = cpuPercent,
                RamPercent = ramPercent,
                DiskPercent = diskPercent,
                NetworkLatencyMs = networkLatencyMs,
                LanIp = GetPrivateLanIp(),
                // Public IP is optional; never block heartbeat delivery on an external lookup.
                PublicIp = null,
                AdJoined = adJoined,
                TrellixInstalled = trellixInstalled,
                DesktopCentralInstalled = desktopCentralInstalled
            });
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            // Telemetry must never interrupt the agent's main workflow.
        }
    }

    [SupportedOSPlatform("windows")]
    public async Task SendInstalledSoftwareAsync()
    {
        try
        {
            var software = new List<object>();
            var uninstallPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var uninstallPath in uninstallPaths)
            {
                using var root = Registry.LocalMachine.OpenSubKey(uninstallPath);
                if (root == null) continue;

                foreach (var subKeyName in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(subKeyName);
                    var softwareName = key?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(softwareName)) continue;

                    software.Add(new
                    {
                        DeviceName = Environment.MachineName,
                        SoftwareName = softwareName.Trim(),
                        Version = key?.GetValue("DisplayVersion") as string,
                        Publisher = key?.GetValue("Publisher") as string,
                        InstallDate = key?.GetValue("InstallDate") as string
                    });
                }
            }

            if (software.Count == 0) return;

            using var response = await _http.PostAsJsonAsync(
                $"{ApiBaseUrl}/api/software/inventory/bulk", software);
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            // Inventory failures are retried at the next scheduled sync.
        }
    }

    private static string? GetPrivateLanIp()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Select(address => address.ToString())
            .FirstOrDefault(ip => ip.StartsWith("10.", StringComparison.Ordinal) ||
                                  ip.StartsWith("192.168.", StringComparison.Ordinal) ||
                                  IsPrivate172Address(ip));
    }

    private static bool IsPrivate172Address(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 &&
               int.TryParse(parts[1], out var secondOctet) &&
               secondOctet is >= 16 and <= 31;
    }

    private static async Task<string?> GetPublicIpAddressAsync()
    {
        try
        {
            return (await _publicIpHttp.GetStringAsync("https://api.ipify.org?format=text")).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static readonly HttpClient _publicIpHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
}

// Cách gọi: đặt ngay trong đoạn code hiển thị popup cảnh báo hiệu năng hiện tại của bạn, ví dụ:
//
//   if (cpuPercent > 80 && sustainedOver5Minutes)
//   {
//       ShowPerformanceWarningPopup("CPU");
//       _ = _telemetryClient.ReportWarningAsync("CPU", cpuPercent, currentSite?.Site, currentSite?.Region);
//   }


// ----------------------------------------------------------------------------
// 5. THÊM MỚI: gửi Heartbeat định kỳ (mỗi 60 giây) để Dashboard IT thấy được
//    danh sách máy + CPU/RAM/Disk hiện tại + trạng thái ANBM (AD Join/Trellix/Desktop Central).
//    Thêm hàm này vào TelemetryClient.cs ở trên.
// ----------------------------------------------------------------------------

/*
    Thêm vào class TelemetryClient:

    public async Task SendHeartbeatAsync(
        string? siteName, string? region, bool isInternal,
        decimal cpuPercent, decimal ramPercent, decimal diskPercent, int networkLatencyMs,
        bool adJoined, bool trellixInstalled, bool desktopCentralInstalled)
    {
        try
        {
            await _http.PostAsJsonAsync($"{ApiBaseUrl}/api/heartbeat", new
            {
                DeviceName = Environment.MachineName,
                SiteName = siteName,
                Region = region,
                IsInternal = isInternal,
                AppVersion = "1.0.0", // lấy từ Assembly version thực tế nếu có
                CpuPercent = cpuPercent,
                RamPercent = ramPercent,
                DiskPercent = diskPercent,
                NetworkLatencyMs = networkLatencyMs,
                LanIp = GetPrivateLanIp(),
                PublicIp = await GetPublicIpAddressAsync(),
                AdJoined = adJoined,
                TrellixInstalled = trellixInstalled,
                DesktopCentralInstalled = desktopCentralInstalled
            });
        }
        catch
        {
            // Mất mạng / server tạm thời không phản hồi - bỏ qua, thử lại ở lần heartbeat kế tiếp
        }
    }

    private static string? GetPrivateLanIp()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            .Select(a => a.ToString())
            .FirstOrDefault(ip => ip.StartsWith("10.", StringComparison.Ordinal) ||
                                  ip.StartsWith("192.168.", StringComparison.Ordinal) ||
                                  ip.StartsWith("172.", StringComparison.Ordinal));
    }

    private static async Task<string?> GetPublicIpAddressAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            return (await http.GetStringAsync("https://api.ipify.org?format=text")).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsInternalNetwork()
    {
        foreach (var ip in NetworkInterface
            .GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)))
        {
            var bytes = ip.GetAddressBytes();
            var isPrivate10 = bytes[0] == 10;
            var isPrivate192 = bytes[0] == 192 && bytes[1] == 168;
            var isPrivate172 = bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;
            if (isPrivate10 || isPrivate192 || isPrivate172)
                return true;
        }

        return false;
    }

    Trong App.xaml.cs, thêm 1 Timer riêng (khác với timer đo hiệu năng hiện có của bạn),
    chạy mỗi 60 giây, LẤY LẠI đúng các giá trị CPU/RAM/Disk/ANBM mà code hiện tại của bạn
    đang dùng để vẽ UI (khung "Hiệu năng máy tính" + "Tuân thủ ANBM" trong popup), rồi gọi:

        _heartbeatTimer = new System.Timers.Timer(60_000);
        _heartbeatTimer.Elapsed += async (_, _) =>
        {
            await _telemetryClient.SendHeartbeatAsync(
                currentSite?.Site, currentSite?.Region, isInternal,
                cpuPercent, ramPercent, diskPercent, networkLatencyMs,
                adJoined, trellixInstalled, desktopCentralInstalled);
        };
        _heartbeatTimer.Start();
*/

// ----------------------------------------------------------------------------
// 6. THÊM MỚI: gửi danh sách phần mềm đã cài lên server
//    Đây là phần "Thông tin phần mềm đã cài" mà doanh nghiệp rất cần.
//    Code này quét các phần mềm trong registry Windows và đẩy lên API.
// ----------------------------------------------------------------------------

/*
    Thêm hàm vào class TelemetryClient:

    using Microsoft.Win32;

    public async Task SendInstalledSoftwareAsync()
    {
        try
        {
            var software = new List<object>();

            foreach (var hive in new[] { "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall" })
            {
                using var root = Registry.LocalMachine.OpenSubKey(hive);
                if (root == null) continue;

                foreach (var subKeyName in root.GetSubKeyNames())
                {
                    try
                    {
                        using var key = root.OpenSubKey(subKeyName);
                        if (key == null) continue;

                        var displayName = key.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        var version = key.GetValue("DisplayVersion") as string;
                        var publisher = key.GetValue("Publisher") as string;
                        var installDate = key.GetValue("InstallDate") as string;

                        software.Add(new
                        {
                            DeviceName = Environment.MachineName,
                            SoftwareName = displayName.Trim(),
                            Version = version,
                            Publisher = publisher,
                            InstallDate = installDate
                        });
                    }
                    catch
                    {
                        // bỏ qua phần mềm không đọc được
                    }
                }
            }

            if (software.Count == 0)
                return;

            await _http.PostAsJsonAsync($"{ApiBaseUrl}/api/software/inventory/bulk", software);
        }
        catch
        {
            // bỏ qua nếu không gửi được
        }
    }

    Gọi hàm này ngay khi máy khởi động và định kỳ mỗi 12 giờ:

        _ = _telemetryClient.SendInstalledSoftwareAsync();

        _softwareSyncTimer = new System.Timers.Timer(12 * 60 * 60 * 1000);
        _softwareSyncTimer.Elapsed += async (_, _) =>
        {
            _ = _telemetryClient.SendInstalledSoftwareAsync();
        };
        _softwareSyncTimer.Start();
*/
