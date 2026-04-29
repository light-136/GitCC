namespace SmartHMI.Core.Interfaces;

public interface IReportService
{
    Task<string> ExportAlarmReportAsync(DateTime from, DateTime to, string outputPath);
    Task<string> ExportProductionReportAsync(DateTime from, DateTime to, string outputPath);
    Task<string> ExportTraceReportAsync(string workorderId, string outputPath);
}
