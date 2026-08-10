using IndustrialVoiceRecorder.Application.Services;
using IndustrialVoiceRecorder.Domain.Entities;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace IndustrialVoiceRecorder.Infrastructure.Export;

/// <summary>
/// Excel导出服务实现
/// 基于EPPlus库实现Excel文件导出功能
/// </summary>
public class ExcelExportService : IExportService
{
    private readonly ILogger<ExcelExportService> _logger;

    public event EventHandler<ExportProgressEventArgs>? ExportProgress;

    public ExcelExportService(ILogger<ExcelExportService> logger)
    {
        _logger = logger;
        // 设置EPPlus许可证上下文（非商业用途）
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    /// <summary>
    /// 导出为Excel
    /// </summary>
    public async Task<string> ExportToExcelAsync(List<VoiceRecord> records, string filePath)
    {
        try
        {
            _logger.LogInformation("开始导出Excel，记录数: {Count}", records.Count);
            ReportProgress(0, "开始导出...");

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("语音记录");

            // 设置表头
            SetupHeader(worksheet);
            ReportProgress(20, "设置表头完成");

            // 填充数据
            await FillDataAsync(worksheet, records);
            ReportProgress(80, "数据填充完成");

            // 设置样式
            FormatWorksheet(worksheet, records.Count);
            ReportProgress(90, "格式设置完成");

            // 确保目录存在
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 保存文件
            await package.SaveAsAsync(new FileInfo(filePath));
            ReportProgress(100, "导出完成");

            _logger.LogInformation("Excel导出成功: {FilePath}", filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出Excel失败");
            throw;
        }
    }

    /// <summary>
    /// 导出为CSV
    /// </summary>
    public async Task<string> ExportToCsvAsync(List<VoiceRecord> records, string filePath)
    {
        try
        {
            _logger.LogInformation("开始导出CSV，记录数: {Count}", records.Count);
            ReportProgress(0, "开始导出...");

            // 确保目录存在
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);

            // 写入表头
            await writer.WriteLineAsync("记录时间,识别文本,产品编号,分类,状态,置信度,操作员,备注");
            ReportProgress(20, "表头写入完成");

            // 写入数据
            int count = 0;
            foreach (var record in records)
            {
                var line = $"\"{record.RecordTime:yyyy-MM-dd HH:mm:ss}\"," +
                          $"\"{EscapeCsv(record.RecognizedText)}\"," +
                          $"\"{record.ProductId}\"," +
                          $"\"{record.Category}\"," +
                          $"\"{record.Status}\"," +
                          $"{record.Confidence:F2}," +
                          $"\"{record.Operator}\"," +
                          $"\"{EscapeCsv(record.Remarks)}\"";

                await writer.WriteLineAsync(line);

                count++;
                if (count % 100 == 0)
                {
                    var progress = 20 + (int)(60.0 * count / records.Count);
                    ReportProgress(progress, $"已导出 {count}/{records.Count} 条记录");
                }
            }

            ReportProgress(100, "导出完成");
            _logger.LogInformation("CSV导出成功: {FilePath}", filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出CSV失败");
            throw;
        }
    }

    /// <summary>
    /// 导出统计报表
    /// </summary>
    public async Task<string> ExportStatisticsAsync(Statistics stats, string filePath)
    {
        try
        {
            _logger.LogInformation("开始导出统计报表");
            ReportProgress(0, "开始导出...");

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("统计报表");

            // 设置标题
            worksheet.Cells["A1"].Value = "语音记录统计报表";
            worksheet.Cells["A1:D1"].Merge = true;
            worksheet.Cells["A1"].Style.Font.Size = 16;
            worksheet.Cells["A1"].Style.Font.Bold = true;
            worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // 统计数据
            worksheet.Cells["A3"].Value = "统计日期:";
            worksheet.Cells["B3"].Value = stats.StatDate.ToString("yyyy-MM-dd");

            worksheet.Cells["A4"].Value = "总记录数:";
            worksheet.Cells["B4"].Value = stats.TotalRecords;

            worksheet.Cells["A5"].Value = "合格数:";
            worksheet.Cells["B5"].Value = stats.OkCount;

            worksheet.Cells["A6"].Value = "不合格数:";
            worksheet.Cells["B6"].Value = stats.NgCount;

            worksheet.Cells["A7"].Value = "合格率:";
            worksheet.Cells["B7"].Value = $"{stats.PassRate:F2}%";

            // 设置样式
            worksheet.Cells["A3:A7"].Style.Font.Bold = true;
            worksheet.Cells["A1:D7"].AutoFitColumns();

            ReportProgress(80, "数据填充完成");

            // 确保目录存在
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await package.SaveAsAsync(new FileInfo(filePath));
            ReportProgress(100, "导出完成");

            _logger.LogInformation("统计报表导出成功: {FilePath}", filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出统计报表失败");
            throw;
        }
    }

    /// <summary>
    /// 设置Excel表头 (v3.0 增强版)
    /// </summary>
    private void SetupHeader(ExcelWorksheet worksheet)
    {
        var headers = new[]
        {
            "序号", "记录时间", "识别原文", "数字化文本", "修改后文本",
            "产品编号", "状态", "分类", "记录人", "复核人", "复核时间",
            "音频文件路径", "备注"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cells[1, i + 1];
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(Color.LightBlue);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }
    }

    /// <summary>
    /// 填充数据 (v3.0 增强版)
    /// </summary>
    private async Task FillDataAsync(ExcelWorksheet worksheet, List<VoiceRecord> records)
    {
        await Task.Run(() =>
        {
            int row = 2;
            foreach (var record in records)
            {
                worksheet.Cells[row, 1].Value = record.SerialNumber;
                worksheet.Cells[row, 2].Value = record.RecordTime.ToString("yyyy-MM-dd HH:mm:ss");
                worksheet.Cells[row, 3].Value = record.RecognizedText;
                worksheet.Cells[row, 4].Value = record.NormalizedText;
                worksheet.Cells[row, 5].Value = record.CorrectedText;
                worksheet.Cells[row, 6].Value = record.ProductId;
                worksheet.Cells[row, 7].Value = record.Status;
                worksheet.Cells[row, 8].Value = record.Category;
                worksheet.Cells[row, 9].Value = record.Operator;
                worksheet.Cells[row, 10].Value = record.CorrectedBy;
                worksheet.Cells[row, 11].Value = record.CorrectedAt?.ToString("yyyy-MM-dd HH:mm:ss");
                worksheet.Cells[row, 12].Value = record.AudioFilePath;
                worksheet.Cells[row, 13].Value = record.Remarks;

                // 根据状态设置行颜色
                if (record.Status == "NG")
                {
                    worksheet.Cells[row, 1, row, 13].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 1, row, 13].Style.Fill.BackgroundColor.SetColor(Color.LightPink);
                }

                row++;

                if (row % 100 == 0)
                {
                    var progress = 20 + (int)(60.0 * (row - 2) / records.Count);
                    ReportProgress(progress, $"已处理 {row - 2}/{records.Count} 条记录");
                }
            }
        });
    }

    /// <summary>
    /// 格式化工作表
    /// </summary>
    private void FormatWorksheet(ExcelWorksheet worksheet, int recordCount)
    {
        // 自动调整列宽
        worksheet.Cells[1, 1, recordCount + 1, 13].AutoFitColumns();

        // 设置边框
        var range = worksheet.Cells[1, 1, recordCount + 1, 13];
        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

        // 冻结首行
        worksheet.View.FreezePanes(2, 1);
    }

    /// <summary>
    /// 转义CSV特殊字符
    /// </summary>
    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("\"", "\"\"");
    }

    /// <summary>
    /// 报告进度
    /// </summary>
    private void ReportProgress(int percentage, string message)
    {
        ExportProgress?.Invoke(this, new ExportProgressEventArgs
        {
            ProgressPercentage = percentage,
            Message = message
        });
    }
}
