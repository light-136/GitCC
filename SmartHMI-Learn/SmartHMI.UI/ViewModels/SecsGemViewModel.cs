using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using System.Collections.ObjectModel;

namespace SmartHMI.UI.ViewModels;

public class SecsGemViewModel : BaseViewModel
{
    private readonly ISecsGemService _secsGem;

    public SecsGemStatus Status => _secsGem.Status;
    public string StateText => _secsGem.Status.State.ToString();

    private string _statusMessage = "SECS/GEM 未启用";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public ObservableCollection<SecsGemMessage> MessageLog { get; } = new();

    public RelayCommand EnableCommand { get; }
    public RelayCommand DisableCommand { get; }
    public RelayCommand GoOnlineCommand { get; }
    public RelayCommand GoOfflineCommand { get; }
    public RelayCommand HeartbeatCommand { get; }
    public RelayCommand SendEventCommand { get; }

    public SecsGemViewModel(ISecsGemService secsGem)
    {
        _secsGem = secsGem;

        _secsGem.MessageSent += (_, m) => AddLog(m);
        _secsGem.MessageReceived += (_, m) => AddLog(m);

        EnableCommand = new RelayCommand(async _ => await Enable());
        DisableCommand = new RelayCommand(async _ => await Disable());
        GoOnlineCommand = new RelayCommand(async _ => await GoOnline());
        GoOfflineCommand = new RelayCommand(async _ => await GoOffline());
        HeartbeatCommand = new RelayCommand(async _ => await Heartbeat());
        SendEventCommand = new RelayCommand(async _ => await SendEvent());
    }

    private async Task Enable()
    {
        await _secsGem.EnableAsync();
        StatusMessage = $"SECS/GEM 已启用，状态：{_secsGem.Status.State}";
        OnPropertyChanged(nameof(StateText));
    }

    private async Task Disable()
    {
        await _secsGem.DisableAsync();
        StatusMessage = "SECS/GEM 已禁用";
        OnPropertyChanged(nameof(StateText));
    }

    private async Task GoOnline()
    {
        var ok = await _secsGem.GoOnlineAsync();
        StatusMessage = ok ? "已上线（Online Remote）" : "上线失败，请先启用";
        OnPropertyChanged(nameof(StateText));
    }

    private async Task GoOffline()
    {
        await _secsGem.GoOfflineAsync();
        StatusMessage = "已下线";
        OnPropertyChanged(nameof(StateText));
    }

    private async Task Heartbeat()
    {
        await _secsGem.SendS1F1HeartbeatAsync();
        StatusMessage = $"心跳发送完成，最后心跳：{_secsGem.Status.LastHeartbeat:HH:mm:ss}";
    }

    private async Task SendEvent()
    {
        await _secsGem.SendEventAsync("PROCESS_COMPLETE", new Dictionary<string, object>
        {
            ["LOTID"] = $"LOT-{DateTime.Now:HHmmss}",
            ["PPID"] = "Recipe-A",
            ["RESULT"] = "OK"
        });
        StatusMessage = "事件 PROCESS_COMPLETE 已上报";
    }

    private void AddLog(SecsGemMessage msg)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            MessageLog.Insert(0, msg);
            if (MessageLog.Count > 100) MessageLog.RemoveAt(MessageLog.Count - 1);
        });
    }
}
