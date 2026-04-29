using SmartHMI.Core.Interfaces;
using System.Collections.ObjectModel;

namespace SmartHMI.UI.ViewModels;

public class ReportViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    private DateTime _fromDate = DateTime.Today.AddDays(-7);
    public DateTime FromDate { get => _fromDate; set => SetField(ref _fromDate, value); }

    private DateTime _toDate = DateTime.Today;
    public DateTime ToDate { get => _toDate; set => SetField(ref _toDate, value); }

    private string _workorderId = "";
    public string WorkorderId { get => _workorderId; set => SetField(ref _workorderId, value); }

    private string _statusMessage = "选择报表类型并点击导出";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }

    public ObservableCollection<string> ExportHistory { get; } = new();

    public RelayCommand ExportAlarmReportCommand { get; }
    public RelayCommand ExportProductionReportCommand { get; }
    public RelayCommand ExportTraceReportCommand { get; }

    public ReportViewModel(IReportService reportService)
    {
        _reportService = reportService;
        ExportAlarmReportCommand = new RelayCommand(async _ => await ExportAlarmReport(), _ => !IsBusy);
        ExportProductionReportCommand = new RelayCommand(async _ => await ExportProductionReport(), _ => !IsBusy);
        ExportTraceReportCommand = new RelayCommand(async _ => await ExportTraceReport(), _ => !IsBusy && !string.IsNullOrWhiteSpace(WorkorderId));
    }

    private async Task ExportAlarmReport()
    {
        IsBusy = true;
        StatusMessage = "正在导出报警报表...";
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"报警报表_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            await _reportService.ExportAlarmReportAsync(FromDate, ToDate, path);
            ExportHistory.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 报警报表 → {path}");
            StatusMessage = $"导出成功：{path}";
        }
        catch (Exception ex) { StatusMessage = $"导出失败：{ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task ExportProductionReport()
    {
        IsBusy = true;
        StatusMessage = "正在导出生产报表...";
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"生产报表_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            await _reportService.ExportProductionReportAsync(FromDate, ToDate, path);
            ExportHistory.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 生产报表 → {path}");
            StatusMessage = $"导出成功：{path}";
        }
        catch (Exception ex) { StatusMessage = $"导出失败：{ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task ExportTraceReport()
    {
        IsBusy = true;
        StatusMessage = $"正在导出工单 {WorkorderId} 追溯报表...";
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"追溯报表_{WorkorderId}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            await _reportService.ExportTraceReportAsync(WorkorderId, path);
            ExportHistory.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 追溯报表 → {path}");
            StatusMessage = $"导出成功：{path}";
        }
        catch (Exception ex) { StatusMessage = $"导出失败：{ex.Message}"; }
        finally { IsBusy = false; }
    }
}
