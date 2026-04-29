using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using System.Collections.ObjectModel;

namespace SmartHMI.UI.ViewModels;

public class AiAssistantViewModel : BaseViewModel
{
    private readonly IAiAssistantService _ai;

    public ObservableCollection<AiSuggestion> Suggestions { get; } = new();
    public ObservableCollection<ChatMessage> ChatHistory { get; } = new();

    private string _chatInput = "";
    public string ChatInput { get => _chatInput; set => SetField(ref _chatInput, value); }

    private string _apiEndpoint = "";
    public string ApiEndpoint { get => _apiEndpoint; set { SetField(ref _apiEndpoint, value); _ai.ApiEndpoint = value; } }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }

    private string _statusMessage = "AI 助手就绪";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public RelayCommand AnalyzeAlarmsCommand { get; }
    public RelayCommand AnalyzeDeviceCommand { get; }
    public RelayCommand AnalyzeProductionCommand { get; }
    public RelayCommand SendChatCommand { get; }
    public RelayCommand ClearChatCommand { get; }

    public AiAssistantViewModel(IAiAssistantService ai)
    {
        _ai = ai;
        _apiEndpoint = _ai.ApiEndpoint;

        _ai.SuggestionGenerated += (_, s) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => Suggestions.Insert(0, s));
        };

        AnalyzeAlarmsCommand = new RelayCommand(async _ => await AnalyzeAlarms(), _ => !IsBusy);
        AnalyzeDeviceCommand = new RelayCommand(async _ => await AnalyzeDevice(), _ => !IsBusy);
        AnalyzeProductionCommand = new RelayCommand(async _ => await AnalyzeProduction(), _ => !IsBusy);
        SendChatCommand = new RelayCommand(async _ => await SendChat(), _ => !IsBusy && !string.IsNullOrWhiteSpace(ChatInput));
        ClearChatCommand = new RelayCommand(_ => ChatHistory.Clear());

        foreach (var s in _ai.GetSuggestions()) Suggestions.Add(s);
    }

    private async Task AnalyzeAlarms()
    {
        IsBusy = true;
        StatusMessage = "正在分析报警...";
        var s = await _ai.AnalyzeAlarmsAsync();
        StatusMessage = s != null ? $"分析完成：{s.Title}" : "当前无需处理的报警";
        IsBusy = false;
    }

    private async Task AnalyzeDevice()
    {
        IsBusy = true;
        StatusMessage = "正在分析设备健康度...";
        await _ai.AnalyzeDeviceHealthAsync();
        StatusMessage = "设备健康度分析完成";
        IsBusy = false;
    }

    private async Task AnalyzeProduction()
    {
        IsBusy = true;
        StatusMessage = "正在分析生产趋势...";
        await _ai.AnalyzeProductionTrendAsync();
        StatusMessage = "生产趋势分析完成";
        IsBusy = false;
    }

    private async Task SendChat()
    {
        var msg = ChatInput;
        ChatInput = "";
        ChatHistory.Add(new ChatMessage { Role = "用户", Content = msg, Timestamp = DateTime.Now });
        IsBusy = true;
        StatusMessage = "AI 正在思考...";
        var reply = await _ai.ChatAsync(msg);
        ChatHistory.Add(new ChatMessage { Role = "AI 助手", Content = reply, Timestamp = DateTime.Now });
        StatusMessage = "AI 助手就绪";
        IsBusy = false;
    }
}

public class ChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public bool IsUser => Role == "用户";
}
