using ClosedXML.Excel;
using SGRNetGuard.Api.Models;

namespace SGRNetGuard.Api.Services;

public class ExcelReportBuilder
{
    public byte[] BuildDeviceReport(DeviceReportDataDto report, string deviceName)
    {
        using var workbook = new XLWorkbook();

        var wsSummary = workbook.Worksheets.Add("TongQuan");
        wsSummary.Cell(1, 1).Value = "Thiết bị";
        wsSummary.Cell(1, 2).Value = "Site";
        wsSummary.Cell(1, 3).Value = "Vùng";
        wsSummary.Cell(1, 4).Value = "Mạng";
        wsSummary.Cell(1, 5).Value = "Online";
        wsSummary.Cell(1, 6).Value = "CPU (%)";
        wsSummary.Cell(1, 7).Value = "RAM (%)";
        wsSummary.Cell(1, 8).Value = "Disk (%)";
        wsSummary.Cell(1, 9).Value = "ANBM AD Join";
        wsSummary.Cell(1, 10).Value = "ANBM Trellix";
        wsSummary.Cell(1, 11).Value = "ANBM Desktop Central";
        wsSummary.Cell(1, 12).Value = "Cảnh báo hôm nay";
        wsSummary.Cell(1, 13).Value = "Cập nhật gần nhất (UTC)";
        wsSummary.Cell(1, 14).Value = "Xuất lúc (UTC)";

        var device = report.Device;
        wsSummary.Cell(2, 1).Value = device?.DeviceName ?? deviceName;
        wsSummary.Cell(2, 2).Value = device?.LastSiteName ?? "(mạng ngoài)";
        wsSummary.Cell(2, 3).Value = device?.LastRegion ?? "-";
        wsSummary.Cell(2, 4).Value = device?.IsInternal == true ? "Nội bộ" : "Ngoài";
        wsSummary.Cell(2, 5).Value = device?.IsOnline == true ? "Online" : "Offline";
        wsSummary.Cell(2, 6).Value = device?.CpuPercent;
        wsSummary.Cell(2, 7).Value = device?.RamPercent;
        wsSummary.Cell(2, 8).Value = device?.DiskPercent;
        wsSummary.Cell(2, 9).Value = ToYesNo(device?.AdJoined);
        wsSummary.Cell(2, 10).Value = ToYesNo(device?.TrellixInstalled);
        wsSummary.Cell(2, 11).Value = ToYesNo(device?.DesktopCentralInstalled);
        wsSummary.Cell(2, 12).Value = device?.WarningsToday ?? 0;
        wsSummary.Cell(2, 13).Value = device?.LastSeenUtc;
        wsSummary.Cell(2, 14).Value = DateTime.UtcNow;

        var wsWarnings = workbook.Worksheets.Add("CanhBao7Ngay");
        wsWarnings.Cell(1, 1).Value = "Thiết bị";
        wsWarnings.Cell(1, 2).Value = "Metric";
        wsWarnings.Cell(1, 3).Value = "Giá trị (%)";
        wsWarnings.Cell(1, 4).Value = "Site";
        wsWarnings.Cell(1, 5).Value = "Vùng";
        wsWarnings.Cell(1, 6).Value = "Thời gian cảnh báo (UTC)";

        for (var i = 0; i < report.Warnings.Count; i++)
        {
            var row = i + 2;
            var warning = report.Warnings[i];
            wsWarnings.Cell(row, 1).Value = deviceName;
            wsWarnings.Cell(row, 2).Value = warning.MetricType;
            wsWarnings.Cell(row, 3).Value = warning.MetricValue;
            wsWarnings.Cell(row, 4).Value = warning.SiteName ?? "(mạng ngoài)";
            wsWarnings.Cell(row, 5).Value = warning.Region ?? "-";
            wsWarnings.Cell(row, 6).Value = warning.WarnedAtUtc;
        }

        StyleWorksheet(wsSummary);
        StyleWorksheet(wsWarnings);
        wsSummary.Columns().AdjustToContents();
        wsWarnings.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void StyleWorksheet(IXLWorksheet worksheet)
    {
        var header = worksheet.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5FC");
    }

    private static string ToYesNo(bool? value)
    {
        if (value == true) return "Có";
        if (value == false) return "Không";
        return "Chưa có dữ liệu";
    }

    public byte[] BuildMultiDeviceReport(
        IReadOnlyList<DeviceDashboardDto> devices,
        IReadOnlyList<DeviceReportWarningDto> warnings,
        string reportTitle)
    {
        using var workbook = new XLWorkbook();

        var wsSummary = workbook.Worksheets.Add("TongQuanMay");
        wsSummary.Cell(1, 1).Value = reportTitle;
        wsSummary.Cell(2, 1).Value = "Xuất lúc (UTC)";
        wsSummary.Cell(2, 2).Value = DateTime.UtcNow;
        wsSummary.Cell(4, 1).Value = "Thiết bị";
        wsSummary.Cell(4, 2).Value = "Site";
        wsSummary.Cell(4, 3).Value = "Vùng";
        wsSummary.Cell(4, 4).Value = "Mạng";
        wsSummary.Cell(4, 5).Value = "Online";
        wsSummary.Cell(4, 6).Value = "CPU (%)";
        wsSummary.Cell(4, 7).Value = "RAM (%)";
        wsSummary.Cell(4, 8).Value = "Disk (%)";
        wsSummary.Cell(4, 9).Value = "ANBM AD Join";
        wsSummary.Cell(4, 10).Value = "ANBM Trellix";
        wsSummary.Cell(4, 11).Value = "ANBM Desktop Central";
        wsSummary.Cell(4, 12).Value = "Cảnh báo hôm nay";
        wsSummary.Cell(4, 13).Value = "Cập nhật gần nhất (UTC)";

        for (var i = 0; i < devices.Count; i++)
        {
            var row = i + 5;
            var device = devices[i];
            wsSummary.Cell(row, 1).Value = device.DeviceName;
            wsSummary.Cell(row, 2).Value = device.LastSiteName ?? "(mạng ngoài)";
            wsSummary.Cell(row, 3).Value = device.LastRegion ?? "-";
            wsSummary.Cell(row, 4).Value = device.IsInternal ? "Nội bộ" : "Ngoài";
            wsSummary.Cell(row, 5).Value = device.IsOnline ? "Online" : "Offline";
            wsSummary.Cell(row, 6).Value = device.CpuPercent;
            wsSummary.Cell(row, 7).Value = device.RamPercent;
            wsSummary.Cell(row, 8).Value = device.DiskPercent;
            wsSummary.Cell(row, 9).Value = ToYesNo(device.AdJoined);
            wsSummary.Cell(row, 10).Value = ToYesNo(device.TrellixInstalled);
            wsSummary.Cell(row, 11).Value = ToYesNo(device.DesktopCentralInstalled);
            wsSummary.Cell(row, 12).Value = device.WarningsToday;
            wsSummary.Cell(row, 13).Value = device.LastSeenUtc;
        }

        var wsWarnings = workbook.Worksheets.Add("CanhBao7Ngay");
        wsWarnings.Cell(1, 1).Value = "Thiết bị";
        wsWarnings.Cell(1, 2).Value = "Metric";
        wsWarnings.Cell(1, 3).Value = "Giá trị (%)";
        wsWarnings.Cell(1, 4).Value = "Site";
        wsWarnings.Cell(1, 5).Value = "Vùng";
        wsWarnings.Cell(1, 6).Value = "Thời gian cảnh báo (UTC)";

        for (var i = 0; i < warnings.Count; i++)
        {
            var row = i + 2;
            var warning = warnings[i];
            wsWarnings.Cell(row, 1).Value = warning.DeviceName;
            wsWarnings.Cell(row, 2).Value = warning.MetricType;
            wsWarnings.Cell(row, 3).Value = warning.MetricValue;
            wsWarnings.Cell(row, 4).Value = warning.SiteName ?? "(mạng ngoài)";
            wsWarnings.Cell(row, 5).Value = warning.Region ?? "-";
            wsWarnings.Cell(row, 6).Value = warning.WarnedAtUtc;
        }

        wsSummary.Row(1).Style.Font.Bold = true;
        wsSummary.Row(4).Style.Font.Bold = true;
        wsSummary.Row(4).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5FC");
        StyleWorksheet(wsWarnings);
        wsSummary.Columns().AdjustToContents();
        wsWarnings.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
