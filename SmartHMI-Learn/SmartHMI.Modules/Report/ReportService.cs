using OfficeOpenXml;
using OfficeOpenXml.Style;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using System.Drawing;
using System.IO;

namespace SmartHMI.Modules.Report;

public class ReportService : IReportService
{
    private readonly IAlarmService _alarmService;
    private readonly ITraceabilityService _traceability;

    public ReportService(IAlarmService alarmService, ITraceabilityService traceability)
    {
        _alarmService = alarmService;
        _traceability = traceability;
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public async Task<string> ExportAlarmReportAsync(DateTime from, DateTime to, string outputPath)
    {
        var alarms = _alarmService.AlarmHistory
            .Where(a => a.TriggeredAt >= from && a.TriggeredAt <= to)
            .ToList();

        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("报警记录");

        WriteHeader(ws, new[] { "报警代码", "消息", "级别", "来源", "触发时间", "确认时间", "清除时间" });

        for (int i = 0; i < alarms.Count; i++)
        {
            var a = alarms[i];
            ws.Cells[i + 2, 1].Value = a.Code;
            ws.Cells[i + 2, 2].Value = a.Message;
            ws.Cells[i + 2, 3].Value = a.Level.ToString();
            ws.Cells[i + 2, 4].Value = a.Source;
            ws.Cells[i + 2, 5].Value = a.TriggeredAt.ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cells[i + 2, 6].Value = a.AcknowledgedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            ws.Cells[i + 2, 7].Value = a.ClearedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        }

        ws.Cells.AutoFitColumns();
        await pkg.SaveAsAsync(new FileInfo(outputPath));
        return outputPath;
    }

    public async Task<string> ExportProductionReportAsync(DateTime from, DateTime to, string outputPath)
    {
        var records = _traceability.GetRecent(1000)
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .ToList();

        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("生产追溯");

        WriteHeader(ws, new[] { "工单号", "序列号", "产品类型", "事件类型", "工序", "结果", "操作员", "工站", "时间" });

        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            ws.Cells[i + 2, 1].Value = r.WorkorderId;
            ws.Cells[i + 2, 2].Value = r.SerialNumber;
            ws.Cells[i + 2, 3].Value = r.ProductType;
            ws.Cells[i + 2, 4].Value = r.EventType.ToString();
            ws.Cells[i + 2, 5].Value = r.StepName;
            ws.Cells[i + 2, 6].Value = r.Result;
            ws.Cells[i + 2, 7].Value = r.OperatorId;
            ws.Cells[i + 2, 8].Value = r.StationId;
            ws.Cells[i + 2, 9].Value = r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        }

        ws.Cells.AutoFitColumns();
        await pkg.SaveAsAsync(new FileInfo(outputPath));
        return outputPath;
    }

    public async Task<string> ExportTraceReportAsync(string workorderId, string outputPath)
    {
        var records = _traceability.GetByWorkorder(workorderId);

        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add($"工单_{workorderId}");

        WriteHeader(ws, new[] { "序列号", "事件类型", "工序", "结果", "操作员", "工站", "时间" });

        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            ws.Cells[i + 2, 1].Value = r.SerialNumber;
            ws.Cells[i + 2, 2].Value = r.EventType.ToString();
            ws.Cells[i + 2, 3].Value = r.StepName;
            ws.Cells[i + 2, 4].Value = r.Result;
            ws.Cells[i + 2, 5].Value = r.OperatorId;
            ws.Cells[i + 2, 6].Value = r.StationId;
            ws.Cells[i + 2, 7].Value = r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        }

        ws.Cells.AutoFitColumns();
        await pkg.SaveAsAsync(new FileInfo(outputPath));
        return outputPath;
    }

    private static void WriteHeader(ExcelWorksheet ws, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cells[1, i + 1].Value = headers[i];
            ws.Cells[1, i + 1].Style.Font.Bold = true;
            ws.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0x16, 0x21, 0x3E));
            ws.Cells[1, i + 1].Style.Font.Color.SetColor(Color.FromArgb(0x00, 0xD4, 0xFF));
        }
    }
}
