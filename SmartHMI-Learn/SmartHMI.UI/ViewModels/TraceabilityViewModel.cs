using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using System.Collections.ObjectModel;

namespace SmartHMI.UI.ViewModels;

public class TraceabilityViewModel : BaseViewModel
{
    private readonly ITraceabilityService _traceability;

    public ObservableCollection<TraceRecord> Records { get; } = new();

    private string _searchSerial = "";
    public string SearchSerial { get => _searchSerial; set => SetField(ref _searchSerial, value); }

    private string _searchWorkorder = "";
    public string SearchWorkorder { get => _searchWorkorder; set => SetField(ref _searchWorkorder, value); }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public RelayCommand SearchBySerialCommand { get; }
    public RelayCommand SearchByWorkorderCommand { get; }
    public RelayCommand LoadRecentCommand { get; }
    public RelayCommand AddSampleCommand { get; }

    public TraceabilityViewModel(ITraceabilityService traceability)
    {
        _traceability = traceability;
        SearchBySerialCommand = new RelayCommand(_ => SearchBySerial());
        SearchByWorkorderCommand = new RelayCommand(_ => SearchByWorkorder());
        LoadRecentCommand = new RelayCommand(_ => LoadRecent());
        AddSampleCommand = new RelayCommand(_ => AddSampleRecord());
        LoadRecent();
    }

    private void LoadRecent()
    {
        Records.Clear();
        foreach (var r in _traceability.GetRecent(100)) Records.Add(r);
        StatusMessage = $"显示最近 {Records.Count} 条追溯记录";
    }

    private void SearchBySerial()
    {
        if (string.IsNullOrWhiteSpace(SearchSerial)) return;
        Records.Clear();
        foreach (var r in _traceability.GetBySerial(SearchSerial)) Records.Add(r);
        StatusMessage = $"序列号 {SearchSerial} 共 {Records.Count} 条记录";
    }

    private void SearchByWorkorder()
    {
        if (string.IsNullOrWhiteSpace(SearchWorkorder)) return;
        Records.Clear();
        foreach (var r in _traceability.GetByWorkorder(SearchWorkorder)) Records.Add(r);
        StatusMessage = $"工单 {SearchWorkorder} 共 {Records.Count} 条记录";
    }

    private void AddSampleRecord()
    {
        var sn = $"SN{DateTime.Now:yyyyMMddHHmmss}";
        _traceability.Record(new TraceRecord
        {
            WorkorderId = $"WO-{DateTime.Now:yyyyMMdd}-001",
            SerialNumber = sn,
            ProductType = "ProductA",
            EventType = TraceEventType.Complete,
            StepName = "最终检验",
            Result = "OK",
            OperatorId = "operator",
            StationId = "ST-01"
        });
        StatusMessage = $"已添加追溯记录：{sn}";
        LoadRecent();
    }
}
