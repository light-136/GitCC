using System.Collections.ObjectModel;
using SmartHMI.Core.Events;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using SmartHMI.Modules.Communication;

namespace SmartHMI.UI.ViewModels;

public class CommunicationViewModel : BaseViewModel
{
    private readonly CommunicationManager _commManager;
    private CommunicationChannel? _selectedChannel;
    private bool _isBusy;
    private string _sendText = "";
    private string _messageLog = "";

    public ObservableCollection<CommunicationChannel> Channels { get; } = new();
    public CommunicationChannel? SelectedChannel { get => _selectedChannel; set => SetField(ref _selectedChannel, value); }
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string SendText { get => _sendText; set => SetField(ref _sendText, value); }
    public string MessageLog { get => _messageLog; set => SetField(ref _messageLog, value); }

    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public CommunicationViewModel(CommunicationManager commManager, IEventBus eventBus)
    {
        _commManager = commManager;

        ConnectCommand = new RelayCommand(async _ => await ConnectAsync(),
            _ => SelectedChannel?.Status == ConnectionStatus.Disconnected && !IsBusy);
        DisconnectCommand = new RelayCommand(async _ => await DisconnectAsync(),
            _ => SelectedChannel?.Status == ConnectionStatus.Connected && !IsBusy);
        SendCommand = new RelayCommand(async _ => await SendAsync(),
            _ => SelectedChannel?.Status == ConnectionStatus.Connected && !string.IsNullOrEmpty(SendText));
        RefreshCommand = new RelayCommand(RefreshChannels);

        _commManager.StatusChanged += (_, e) => App.Current.Dispatcher.Invoke(RefreshChannels);
        _commManager.DataReceived += (_, e) => App.Current.Dispatcher.Invoke(() =>
            AppendLog($"[收] {e.ChannelId}: {System.Text.Encoding.UTF8.GetString(e.Data)}"));

        eventBus.Subscribe<CommunicationStatusChangedEvent>(e =>
            App.Current.Dispatcher.Invoke(() =>
                AppendLog($"[状态] {e.ChannelName}: {e.Status}")));

        RefreshChannels();
    }

    private void RefreshChannels()
    {
        var selected = SelectedChannel?.Id;
        Channels.Clear();
        foreach (var ch in _commManager.Channels)
            Channels.Add(ch);
        SelectedChannel = Channels.FirstOrDefault(c => c.Id == selected) ?? Channels.FirstOrDefault();
    }

    private async Task ConnectAsync()
    {
        if (SelectedChannel == null) return;
        IsBusy = true;
        AppendLog($"[连接] 正在连接 {SelectedChannel.Name}...");
        await _commManager.ConnectAsync(SelectedChannel.Id);
        IsBusy = false;
        RefreshChannels();
    }

    private async Task DisconnectAsync()
    {
        if (SelectedChannel == null) return;
        IsBusy = true;
        await _commManager.DisconnectAsync(SelectedChannel.Id);
        IsBusy = false;
        RefreshChannels();
    }

    private async Task SendAsync()
    {
        if (SelectedChannel == null || string.IsNullOrEmpty(SendText)) return;
        var data = System.Text.Encoding.UTF8.GetBytes(SendText);
        await _commManager.SendAsync(SelectedChannel.Id, data);
        AppendLog($"[发] {SelectedChannel.Name}: {SendText}");
        SendText = "";
    }

    private void AppendLog(string msg)
    {
        MessageLog = $"[{DateTime.Now:HH:mm:ss}] {msg}\n" + MessageLog;
        if (MessageLog.Length > 5000)
            MessageLog = MessageLog[..4000];
    }
}
