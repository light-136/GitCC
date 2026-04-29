using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using System.Collections.ObjectModel;

namespace SmartHMI.UI.ViewModels;

public class MesViewModel : BaseViewModel
{
    private readonly IMesConnector _mes;

    public bool IsConnected => _mes.IsConnected;

    private WorkorderModel? _currentWorkorder;
    public WorkorderModel? CurrentWorkorder { get => _currentWorkorder; set { SetField(ref _currentWorkorder, value); OnPropertyChanged(nameof(HasWorkorder)); } }

    public bool HasWorkorder => _currentWorkorder != null;

    private string _statusMessage = "未连接 MES";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    private int _reportQty = 1;
    public int ReportQty { get => _reportQty; set => SetField(ref _reportQty, value); }

    private int _reportNgQty;
    public int ReportNgQty { get => _reportNgQty; set => SetField(ref _reportNgQty, value); }

    public ObservableCollection<string> ActivityLog { get; } = new();

    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand RefreshWorkorderCommand { get; }
    public RelayCommand ReportProductionCommand { get; }

    public MesViewModel(IMesConnector mes)
    {
        _mes = mes;
        _mes.WorkorderReceived += (_, wo) => { CurrentWorkorder = wo; Log($"收到工单：{wo.Id} ({wo.ProductType})"); };

        ConnectCommand = new RelayCommand(async _ => await Connect(), _ => !_mes.IsConnected);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => _mes.IsConnected);
        RefreshWorkorderCommand = new RelayCommand(async _ => await RefreshWorkorder(), _ => _mes.IsConnected);
        ReportProductionCommand = new RelayCommand(async _ => await ReportProduction(), _ => _mes.IsConnected && HasWorkorder);
    }

    private async Task Connect()
    {
        StatusMessage = "正在连接 MES...";
        var ok = await _mes.ConnectAsync();
        StatusMessage = ok ? "MES 连接成功" : "MES 连接失败";
        OnPropertyChanged(nameof(IsConnected));
        if (ok) Log("MES 连接成功");
    }

    private void Disconnect()
    {
        _mes.Disconnect();
        CurrentWorkorder = null;
        StatusMessage = "已断开 MES 连接";
        OnPropertyChanged(nameof(IsConnected));
        Log("MES 已断开");
    }

    private async Task RefreshWorkorder()
    {
        CurrentWorkorder = await _mes.GetCurrentWorkorderAsync();
        StatusMessage = CurrentWorkorder != null ? $"当前工单：{CurrentWorkorder.Id}" : "无活动工单";
    }

    private async Task ReportProduction()
    {
        if (_currentWorkorder == null) return;
        var ok = await _mes.ReportProductionAsync(_currentWorkorder.Id, ReportQty, ReportNgQty);
        StatusMessage = ok ? $"已上报：OK={ReportQty} NG={ReportNgQty}" : "上报失败";
        Log($"生产上报：OK={ReportQty} NG={ReportNgQty} → {(ok ? "成功" : "失败")}");
        if (ok) await RefreshWorkorder();
    }

    private void Log(string msg)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            ActivityLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
            if (ActivityLog.Count > 100) ActivityLog.RemoveAt(ActivityLog.Count - 1);
        });
    }
}
