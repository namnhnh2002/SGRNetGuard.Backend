using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using SGRNetGuard.Api.Data;
using SGRNetGuard.Api.Hubs;
using SGRNetGuard.Api.Models;
using SGRNetGuard.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SqlDataAccess>();
builder.Services.AddSingleton<ExcelReportBuilder>();
builder.Services.AddSignalR();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var settingsFilePath = Path.Combine(AppContext.BaseDirectory, "system-settings.json");

SystemSettingsDto LoadSettings()
{
    if (!File.Exists(settingsFilePath))
    {
        var defaults = new SystemSettingsDto();
        File.WriteAllText(settingsFilePath, System.Text.Json.JsonSerializer.Serialize(defaults, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return defaults;
    }

    try
    {
        var json = File.ReadAllText(settingsFilePath);
        var settings = System.Text.Json.JsonSerializer.Deserialize<SystemSettingsDto>(json);
        return settings ?? new SystemSettingsDto();
    }
    catch
    {
        var defaults = new SystemSettingsDto();
        File.WriteAllText(settingsFilePath, System.Text.Json.JsonSerializer.Serialize(defaults, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return defaults;
    }
}

void SaveSettings(SystemSettingsDto settings)
{
    File.WriteAllText(settingsFilePath, System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

// Cho phép app trên máy user (chạy như 1 process riêng) và trình duyệt Dashboard
// gọi tới API này từ các origin khác nhau trong mạng nội bộ.
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
    [
        "http://localhost:5080",
        "http://127.0.0.1:5080",
        "https://localhost",
        "https://127.0.0.1"
    ];

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
    {
        if (configuredOrigins.Contains("*", StringComparer.OrdinalIgnoreCase))
        {
            policy.SetIsOriginAllowed(_ => true);
        }
        else
        {
            foreach (var origin in configuredOrigins.Where(o => !string.IsNullOrWhiteSpace(o)))
            {
                policy.WithOrigins(origin);
            }
        }

        policy.AllowAnyHeader();
        policy.AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors("DefaultCors");

var currentSettings = LoadSettings();
var dashboardAuthEnabled = app.Configuration.GetValue<bool>("DashboardAuth:Enabled", false);
var dashboardUsername = app.Configuration["DashboardAuth:Username"] ?? currentSettings.DashboardUsername;
var dashboardPassword = app.Configuration["DashboardAuth:Password"] ?? currentSettings.DashboardPassword;
const string DashboardAuthCookie = "sgr_dashboard_auth";
var dashboardSessions = new ConcurrentDictionary<string, DateTime>();

bool IsDashboardProtectedPath(PathString path)
{
    var p = path.Value?.ToLowerInvariant() ?? "";
    if (string.IsNullOrWhiteSpace(p)) return false;

    // Allow static asset files (css, js, images, fonts) to be served without dashboard auth
    try
    {
        var ext = System.IO.Path.GetExtension(p);
        if (!string.IsNullOrEmpty(ext))
        {
            var staticExts = new[] { ".css", ".js", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".woff", ".woff2", ".ttf", ".map" };
            if (staticExts.Contains(ext))
                return false;
        }
    }
    catch
    {
        // ignore and fall back to protecting the path
    }

    // Protect main dashboard pages, APIs and hubs
    return p == "/" ||
           p == "/index.html" ||
           p == "/device.html" ||
           p == "/agent.html" ||
           p.StartsWith("/api/") ||
           p.StartsWith("/hubs/");
}

bool HasValidDashboardSession(HttpContext context)
{
    if (!context.Request.Cookies.TryGetValue(DashboardAuthCookie, out var token) || string.IsNullOrWhiteSpace(token))
        return false;

    if (!dashboardSessions.TryGetValue(token, out var expiresAtUtc))
        return false;

    if (expiresAtUtc <= DateTime.UtcNow)
    {
        dashboardSessions.TryRemove(token, out _);
        return false;
    }

    return true;
}

string BuildLoginHtml(string? errorText = null)
{
    var errorBlock = string.IsNullOrWhiteSpace(errorText)
        ? ""
        : $"<div style='margin-bottom:12px;padding:10px;border-radius:8px;background:#fff1f3;color:#b42318;font-size:13px;'>{System.Net.WebUtility.HtmlEncode(errorText)}</div>";

    return $"""
<!doctype html>
<html lang="vi">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Đăng nhập SGR NetGuard</title>
</head>
<body style="margin:0;font-family:Segoe UI,Arial,sans-serif;background:#f3f6fb;display:flex;align-items:center;justify-content:center;min-height:100vh;">
  <form method="post" action="/login" style="width:360px;background:#fff;border:1px solid #d8e0ed;border-radius:12px;padding:22px;box-shadow:0 6px 26px rgba(16,24,40,.08);">
    <h2 style="margin:0 0 16px;font-size:20px;color:#0f1f42;">Đăng nhập SGR NetGuard</h2>
    {errorBlock}
    <label style="display:block;font-size:13px;margin-bottom:6px;color:#344054;">Tài khoản</label>
    <input name="username" autocomplete="username" required style="width:100%;height:38px;padding:0 10px;margin-bottom:12px;border:1px solid #cfd8e6;border-radius:8px;" />
    <label style="display:block;font-size:13px;margin-bottom:6px;color:#344054;">Mật khẩu</label>
    <input type="password" name="password" autocomplete="current-password" required style="width:100%;height:38px;padding:0 10px;margin-bottom:16px;border:1px solid #cfd8e6;border-radius:8px;" />
    <button type="submit" style="width:100%;height:40px;border:0;border-radius:8px;background:#18346b;color:#fff;font-weight:700;cursor:pointer;">Đăng nhập</button>
  </form>
</body>
</html>
""";
}

app.MapGet("/login", () => Results.Content(BuildLoginHtml(), "text/html; charset=utf-8"));

app.MapPost("/login", async (HttpContext context) =>
{
    if (!context.Request.HasFormContentType)
        return Results.Content(BuildLoginHtml("Dữ liệu đăng nhập không hợp lệ."), "text/html; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);

    var form = await context.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    if (!string.Equals(username, dashboardUsername, StringComparison.Ordinal) ||
        !string.Equals(password, dashboardPassword, StringComparison.Ordinal))
    {
        return Results.Content(BuildLoginHtml("Sai tài khoản hoặc mật khẩu."), "text/html; charset=utf-8", statusCode: StatusCodes.Status401Unauthorized);
    }

    var token = Guid.NewGuid().ToString("N");
    var expiresAtUtc = DateTime.UtcNow.AddHours(12);
    dashboardSessions[token] = expiresAtUtc;

    context.Response.Cookies.Append(DashboardAuthCookie, token, new CookieOptions
    {
        HttpOnly = true,
        IsEssential = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Expires = expiresAtUtc
    });

    return Results.Redirect("/");
});

app.MapGet("/logout", (HttpContext context) =>
{
    if (context.Request.Cookies.TryGetValue(DashboardAuthCookie, out var token) && !string.IsNullOrWhiteSpace(token))
    {
        dashboardSessions.TryRemove(token, out _);
    }

    context.Response.Cookies.Delete(DashboardAuthCookie);
    return Results.Redirect("/login");
});

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";

    // Agent endpoints always remain public so desktop clients can send heartbeat and telemetry.
    var unauthenticatedAgentPrefixes = new[]
    {
        "/api/heartbeat",
        "/api/software/inventory",
        "/api/telemetry/warning",
        "/api/network",
        "/api/performance/log",
        "/api/compliance",
        "/api/config",
        "/api/agent/status"
    };

    foreach (var prefix in unauthenticatedAgentPrefixes)
    {
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }
    }

    // Dashboard auth is disabled by default so the intranet dashboard can load immediately.
    // If explicitly enabled in config, require a valid cookie session for dashboard APIs/hubs.
    if (!dashboardAuthEnabled)
    {
        await next();
        return;
    }

    if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
    {
        if (HasValidDashboardSession(context))
        {
            await next();
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { ok = false, message = "Unauthorized" });
        return;
    }

    await next();
});

app.UseDefaultFiles();   // cho phép "/" tự trả về wwwroot/index.html (trang Dashboard)
app.UseStaticFiles();    // phục vụ dashboard.js, style.css trong wwwroot/

static IResult DatabaseUnavailable(string message) => Results.Json(new
{
    ok = false,
    databaseAvailable = false,
    message
}, statusCode: StatusCodes.Status503ServiceUnavailable);

// ============================================================
// GET /api/config
// Client (SGR NetGuard trên máy user) gọi endpoint này định kỳ (khi khởi động
// + mỗi vài tiếng) để lấy danh sách Site/DNS/SSID mới nhất, thay cho config.json tĩnh.
// ============================================================
app.MapGet("/api/config", async (SqlDataAccess db) =>
{
    try
    {
        var config = await db.GetActiveConfigAsync();
        return Results.Ok(config);
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không lấy được config từ database: {ex.Message}");
    }
});

app.MapGet("/api/settings", () => Results.Ok(LoadSettings()));

app.MapPost("/api/settings", (SystemSettingsDto settings) =>
{
    SaveSettings(settings);
    dashboardUsername = settings.DashboardUsername;
    dashboardPassword = settings.DashboardPassword;
    currentSettings = settings;
    return Results.Ok(settings);
});

// ============================================================
// POST /api/telemetry/warning
// Client gọi mỗi khi Popup "Cảnh báo hiệu năng hệ thống" hiển thị trên máy user.
// Sau khi lưu DB, phát ngay sự kiện realtime tới mọi trình duyệt Dashboard IT đang mở.
// ============================================================
app.MapPost("/api/telemetry/warning", async (PerformanceWarningDto dto, SqlDataAccess db, IHubContext<AlertsHub> hub) =>
{
    if (string.IsNullOrWhiteSpace(dto.DeviceName) || string.IsNullOrWhiteSpace(dto.MetricType))
        return Results.BadRequest("Thiếu DeviceName hoặc MetricType");

    try
    {
        await db.InsertWarningAsync(dto);

        // Đẩy realtime tới Dashboard - JS phía client lắng nghe sự kiện "NewWarning"
        await hub.Clients.All.SendAsync("NewWarning", new
        {
            dto.DeviceName,
            dto.SiteName,
            dto.Region,
            dto.MetricType,
            dto.MetricValue,
            Timestamp = DateTime.UtcNow
        });

        return Results.Ok();
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không ghi được cảnh báo vào database: {ex.Message}");
    }
});

// ============================================================
// POST /api/heartbeat
// Client gọi định kỳ (khuyến nghị mỗi 60 giây) để báo: đang online, đứng site nào,
// chỉ số CPU/RAM/Disk hiện tại, tình trạng tuân thủ ANBM.
// Dashboard đọc dữ liệu này qua GET /api/devices.
// ============================================================
app.MapPost("/api/heartbeat", async (HeartbeatDto dto, SqlDataAccess db) =>
{
    if (string.IsNullOrWhiteSpace(dto.DeviceName) && string.IsNullOrWhiteSpace(dto.ComputerName))
        return Results.BadRequest("Thiếu DeviceName");

    try
    {
        // Log incoming heartbeat to file for debugging
        try
        {
            var dump = System.Text.Json.JsonSerializer.Serialize(new { ReceivedAt = DateTime.UtcNow, Payload = dto }, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            var logPath = Path.Combine(AppContext.BaseDirectory, "incoming_requests.log");
            File.AppendAllText(logPath, dump + Environment.NewLine);
        }
        catch { }

        await db.UpsertHeartbeatAsync(dto);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không ghi được heartbeat vào database: {ex.Message}");
    }
});

app.MapPost("/api/network", async (NetworkStatusDto dto, SqlDataAccess db) =>
{
    if (string.IsNullOrWhiteSpace(dto.DeviceName))
        return Results.BadRequest("Thiếu DeviceName");

    try
    {
        await db.RecordNetworkStatusAsync(dto);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không ghi được trạng thái mạng: {ex.Message}");
    }
});

app.MapPost("/api/performance/log", async (PerformanceLogDto dto, SqlDataAccess db) =>
{
    if (string.IsNullOrWhiteSpace(dto.DeviceName))
        return Results.BadRequest("Thiếu DeviceName");

    try
    {
        await db.RecordPerformanceLogAsync(dto);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không ghi được log hiệu năng: {ex.Message}");
    }
});

app.MapPost("/api/compliance", async (ComplianceStatusDto dto, SqlDataAccess db) =>
{
    if (string.IsNullOrWhiteSpace(dto.DeviceName))
        return Results.BadRequest("Thiếu DeviceName");

    try
    {
        await db.RecordComplianceStatusAsync(dto);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không ghi được trạng thái tuân thủ: {ex.Message}");
    }
});

app.MapPost("/api/software/inventory", async (SoftwareInventoryEntryDto dto, SqlDataAccess db) =>
{
    if (string.IsNullOrWhiteSpace(dto.DeviceName) || string.IsNullOrWhiteSpace(dto.SoftwareName))
        return Results.BadRequest("Thiếu DeviceName hoặc SoftwareName");

    try
    {
        // Log incoming software inventory item for debugging
        try
        {
            var dump = System.Text.Json.JsonSerializer.Serialize(new { ReceivedAt = DateTime.UtcNow, Payload = dto }, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            var logPath = Path.Combine(AppContext.BaseDirectory, "incoming_requests.log");
            File.AppendAllText(logPath, dump + Environment.NewLine);
        }
        catch { }

        await db.UpsertSoftwareInventoryAsync(dto);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không ghi được software inventory: {ex.Message}");
    }
});

app.MapPost("/api/software/inventory/bulk", async (List<SoftwareInventoryEntryDto> dtos, SqlDataAccess db) =>
{
    if (dtos == null || dtos.Count == 0)
        return Results.BadRequest("Thiếu danh sách phần mềm");

    try
    {
        // Log bulk items for debugging
        try
        {
            var dump = System.Text.Json.JsonSerializer.Serialize(new { ReceivedAt = DateTime.UtcNow, Count = dtos.Count, Sample = dtos.Take(5) }, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            var logPath = Path.Combine(AppContext.BaseDirectory, "incoming_requests.log");
            File.AppendAllText(logPath, dump + Environment.NewLine);
        }
        catch { }

        await db.UpsertSoftwareInventoryBulkAsync(dtos);
        return Results.Ok(new { ok = true, processed = dtos.Count });
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không ghi được software inventory bulk: {ex.Message}");
    }
});

app.MapPost("/api/agent/preferences", async (DevicePreferenceRequestDto dto, SqlDataAccess db) =>
{
    if (string.IsNullOrWhiteSpace(dto.DeviceName) && string.IsNullOrWhiteSpace(dto.DeviceId))
        return Results.BadRequest("Thiếu DeviceName hoặc DeviceId");

    try
    {
        await db.UpdateDevicePreferenceAsync(dto);
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không cập nhật preference của thiết bị: {ex.Message}");
    }
});

app.MapGet("/api/agent/status", async (string? deviceName, SqlDataAccess db, IConfiguration config) =>
{
    try
    {
        var cpuWarningPercent = config.GetValue<decimal>("Monitoring:Performance:CpuWarningPercent", 85m);
        var ramWarningPercent = config.GetValue<decimal>("Monitoring:Performance:RamWarningPercent", 85m);
        var diskWarningPercent = config.GetValue<decimal>("Monitoring:Performance:DiskWarningPercent", 90m);
        var diskCriticalPercent = config.GetValue<decimal>("Monitoring:Performance:DiskCriticalPercent", 95m);
        var diskIoWarning = config.GetValue<int>("Monitoring:Performance:DiskIoWarning", 80);

        var status = await db.GetAgentStatusAsync(deviceName, cpuWarningPercent, ramWarningPercent, diskWarningPercent, diskCriticalPercent, diskIoWarning);
        if (status == null)
            return Results.NotFound("Không tìm thấy thiết bị");

        return Results.Ok(status);
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không lấy được trạng thái agent: {ex.Message}");
    }
});

// ============================================================
// GET /api/devices
// Dashboard IT gọi để lấy danh sách toàn bộ máy + trạng thái mới nhất (poll mỗi 15-30s
// làm nền, kết hợp với SignalR để có cả 2: danh sách tổng quan + cảnh báo tức thời).
// ============================================================
app.MapGet("/api/devices", async (SqlDataAccess db) =>
{
    try
    {
        var devices = await db.GetDashboardDevicesAsync();
        return Results.Ok(devices);
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không tải được danh sách thiết bị: {ex.Message}");
    }
});

app.MapGet("/api/alerts/today", async (SqlDataAccess db) =>
{
    try
    {
        var warnings = await db.GetTodayWarningsAsync();
        return Results.Ok(warnings);
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không tải được cảnh báo hôm nay: {ex.Message}");
    }
});

app.MapGet("/api/devices/{deviceName}", async (string deviceName, SqlDataAccess db) =>
{
    if (string.IsNullOrWhiteSpace(deviceName))
        return Results.BadRequest("Thiếu DeviceName");

    try
    {
        var detail = await db.GetDeviceDetailAsync(deviceName.Trim());
        if (detail == null)
            return Results.NotFound("Không tìm thấy thiết bị.");
        return Results.Ok(detail);
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không tải được chi tiết thiết bị: {ex.Message}");
    }
});

app.MapGet("/api/devices/{deviceName}/software", async (string deviceName, SqlDataAccess db) =>
{
    if (string.IsNullOrWhiteSpace(deviceName))
        return Results.BadRequest("Thiếu DeviceName");

    try
    {
        var software = await db.GetSoftwareInventoryAsync(deviceName.Trim());
        return Results.Ok(software);
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không tải được danh sách phần mềm: {ex.Message}");
    }
});

// ============================================================
// GET /api/reports/weekly
// ============================================================
app.MapGet("/api/reports/weekly", async (SqlDataAccess db) =>
{
    try
    {
        var data = await db.GetWeeklyReportAsync();
        return Results.Ok(data);
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không tải được báo cáo tuần: {ex.Message}");
    }
});

app.MapGet("/api/reports/device/{deviceName}/excel", async (string deviceName, SqlDataAccess db, ExcelReportBuilder excelBuilder) =>
{
    if (string.IsNullOrWhiteSpace(deviceName))
        return Results.BadRequest("Thiếu DeviceName");

    try
    {
        var report = await db.GetDeviceReportAsync(deviceName);
        if (report.Device == null)
            return Results.NotFound("Không tìm thấy dữ liệu máy này.");

        var bytes = excelBuilder.BuildDeviceReport(report, deviceName);
        var safeName = string.Concat(deviceName.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "device-report";

        return Results.File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{safeName}-report.xlsx");
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không xuất được báo cáo Excel: {ex.Message}");
    }
});

app.MapGet("/api/reports/devices/excel", async (SqlDataAccess db, ExcelReportBuilder excelBuilder) =>
{
    try
    {
        var (devices, warnings) = await db.GetMultiDeviceReportAsync(null);
        if (devices.Count == 0)
            return Results.NotFound("Không có dữ liệu máy để xuất báo cáo.");

        var bytes = excelBuilder.BuildMultiDeviceReport(devices, warnings, "Báo cáo tổng tất cả máy");
        var fileName = $"all-devices-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        return Results.File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không xuất được báo cáo Excel ALL: {ex.Message}");
    }
});

app.MapPost("/api/reports/devices/excel", async (DeviceBulkReportRequestDto request, SqlDataAccess db, ExcelReportBuilder excelBuilder) =>
{
    var names = request.DeviceNames
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (names.Count == 0)
        return Results.BadRequest("Thiếu danh sách máy cần xuất.");

    try
    {
        var (devices, warnings) = await db.GetMultiDeviceReportAsync(names);
        if (devices.Count == 0)
            return Results.NotFound("Không tìm thấy dữ liệu các máy đã chọn.");

        var bytes = excelBuilder.BuildMultiDeviceReport(devices, warnings, $"Báo cáo {devices.Count} máy được chọn");
        var fileName = $"selected-devices-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        return Results.File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
    catch (Exception ex)
    {
        return DatabaseUnavailable($"Không xuất được báo cáo Excel máy đã chọn: {ex.Message}");
    }
});

app.MapGet("/api/health/db", async (SqlDataAccess db) =>
{
    var connectionString = app.Configuration.GetConnectionString("SGRNetGuard");
    return Results.Ok(new
    {
        ok = !string.IsNullOrWhiteSpace(connectionString),
        databaseAvailable = false,
        message = "Endpoint hoạt động. Kết nối DB sẽ được kiểm tra qua /api/devices, /api/config hoặc dashboard refresh."
    });
});

app.MapHub<AlertsHub>("/hubs/alerts");

app.Run();
