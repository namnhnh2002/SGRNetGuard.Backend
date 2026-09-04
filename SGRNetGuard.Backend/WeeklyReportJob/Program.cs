using ClosedXML.Excel;
using Dapper;
using MailKit.Net.Smtp;
using MailKit.Security;
using Npgsql;
using Microsoft.Extensions.Configuration;
using MimeKit;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var connectionString = config.GetConnectionString("SGRNetGuard") ?? config["DATABASE_URL"]
    ?? throw new InvalidOperationException("Thiếu ConnectionStrings:SGRNetGuard");

Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Bắt đầu tạo báo cáo hiệu năng hàng tuần...");

// 1. Lấy dữ liệu từ view vw_WeeklyPerformanceReport (7 ngày gần nhất)
List<WeeklyRow> rows;
using (var conn = new NpgsqlConnection(connectionString))
{
    rows = (await conn.QueryAsync<WeeklyRow>(
        "SELECT * FROM public.vw_WeeklyPerformanceReport ORDER BY TotalWarningCount DESC")).ToList();
}

Console.WriteLine($"Tìm thấy {rows.Count} máy có cảnh báo hiệu năng trong 7 ngày qua.");

// 2. Xuất ra file Excel
var weekStart = DateTime.Today.AddDays(-7).ToString("dd-MM-yyyy");
var weekEnd = DateTime.Today.ToString("dd-MM-yyyy");
var fileName = $"BaoCaoHieuNang_{DateTime.Today:yyyyMMdd}.xlsx";
var filePath = Path.Combine(AppContext.BaseDirectory, fileName);

using (var workbook = new XLWorkbook())
{
    var ws = workbook.Worksheets.Add("Báo cáo hiệu năng");

    ws.Cell(1, 1).Value = $"BÁO CÁO CẢNH BÁO HIỆU NĂNG MÁY TÍNH - Từ {weekStart} đến {weekEnd}";
    ws.Range(1, 1, 1, 7).Merge().Style.Font.SetBold().Font.SetFontSize(14);

    string[] headers = { "STT", "Tên máy", "Site", "Vùng", "Số lần cảnh báo CPU", "Số lần cảnh báo RAM", "Số lần cảnh báo Disk", "Tổng số lần cảnh báo", "Cảnh báo gần nhất" };
    for (int i = 0; i < headers.Length; i++)
    {
        ws.Cell(3, i + 1).Value = headers[i];
        ws.Cell(3, i + 1).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#0F6CBD"));
        ws.Cell(3, i + 1).Style.Font.SetFontColor(XLColor.White);
    }

    int r = 4;
    int stt = 1;
    foreach (var row in rows)
    {
        ws.Cell(r, 1).Value = stt++;
        ws.Cell(r, 2).Value = row.DeviceName;
        ws.Cell(r, 3).Value = row.SiteName ?? "(mạng ngoài)";
        ws.Cell(r, 4).Value = row.Region ?? "-";
        ws.Cell(r, 5).Value = row.CpuWarningCount;
        ws.Cell(r, 6).Value = row.RamWarningCount;
        ws.Cell(r, 7).Value = row.DiskWarningCount;
        ws.Cell(r, 8).Value = row.TotalWarningCount;
        ws.Cell(r, 9).Value = row.LastWarningUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

        if (row.TotalWarningCount >= 20)
            ws.Range(r, 1, r, 9).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FDE7E9")); // đỏ nhạt: cần chú ý

        r++;
    }

    ws.Columns().AdjustToContents();
    workbook.SaveAs(filePath);
}

Console.WriteLine($"Đã tạo file: {filePath}");

// 3. Gửi email đính kèm file Excel
var emailSection = config.GetSection("Email");
var toAddresses = emailSection.GetSection("ToAddresses").Get<string[]>() ?? Array.Empty<string>();

if (toAddresses.Length == 0)
{
    Console.WriteLine("Không có địa chỉ email người nhận (Email:ToAddresses) - bỏ qua bước gửi mail.");
}
else
{
    var message = new MimeMessage();
    message.From.Add(new MailboxAddress(emailSection["FromName"], emailSection["FromAddress"]));
    foreach (var to in toAddresses)
        message.To.Add(MailboxAddress.Parse(to));

    message.Subject = $"[SGR NetGuard] Báo cáo cảnh báo hiệu năng máy tính - Tuần {weekStart} đến {weekEnd}";

    var body = new BodyBuilder
    {
        HtmlBody = $@"
            <p>Chào IT team,</p>
            <p>Đính kèm là báo cáo tổng hợp số lần cảnh báo hiệu năng (CPU/RAM/Disk sử dụng cao liên tục) 
            trên các máy tính từ <b>{weekStart}</b> đến <b>{weekEnd}</b>.</p>
            <p>Tổng số máy có cảnh báo trong tuần: <b>{rows.Count}</b></p>
            <p>Email này được gửi tự động bởi SGR NetGuard.</p>"
    };
    body.Attachments.Add(fileName, File.ReadAllBytes(filePath));
    message.Body = body.ToMessageBody();

    using var client = new SmtpClient();
    var smtpPort = int.Parse(emailSection["SmtpPort"] ?? "587");
    var useSsl = bool.Parse(emailSection["UseSsl"] ?? "true");

    await client.ConnectAsync(emailSection["SmtpHost"], smtpPort,
        useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);

    if (!string.IsNullOrEmpty(emailSection["SmtpUser"]))
        await client.AuthenticateAsync(emailSection["SmtpUser"], emailSection["SmtpPassword"]);

    await client.SendAsync(message);
    await client.DisconnectAsync(true);

    Console.WriteLine($"Đã gửi email báo cáo tới: {string.Join(", ", toAddresses)}");
}

Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hoàn tất.");

// ---------------- Model khớp với view vw_WeeklyPerformanceReport ----------------
class WeeklyRow
{
    public string DeviceName { get; set; } = "";
    public string? SiteName { get; set; }
    public string? Region { get; set; }
    public int CpuWarningCount { get; set; }
    public int RamWarningCount { get; set; }
    public int DiskWarningCount { get; set; }
    public int TotalWarningCount { get; set; }
    public DateTime LastWarningUtc { get; set; }
}
