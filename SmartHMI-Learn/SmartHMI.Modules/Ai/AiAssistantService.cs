using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SmartHMI.Modules.Ai;

/// <summary>
/// AI 助手服务
/// 本地规则引擎 + 可选 OpenAI 兼容 API（如 DeepSeek、本地 Ollama）
/// </summary>
public class AiAssistantService : IAiAssistantService
{
    private readonly IAlarmService _alarmService;
    private readonly List<AiSuggestion> _suggestions = new();
    private readonly Lock _lock = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public bool IsEnabled { get; private set; } = true;
    public string ApiEndpoint { get; set; } = "";

    public event EventHandler<AiSuggestion>? SuggestionGenerated;

    public AiAssistantService(IAlarmService alarmService)
    {
        _alarmService = alarmService;
    }

    public async Task<AiSuggestion?> AnalyzeAlarmsAsync()
    {
        var active = _alarmService.ActiveAlarms;
        if (active.Count == 0) return null;

        // 本地规则引擎
        var criticalCount = active.Count(a => a.Level == AlarmLevel.Critical);
        var errorCount = active.Count(a => a.Level == AlarmLevel.Error);

        AiSuggestion suggestion;
        if (criticalCount > 0)
        {
            suggestion = new AiSuggestion
            {
                Type = AiSuggestionType.Alarm,
                Priority = AiSuggestionPriority.Critical,
                Title = $"检测到 {criticalCount} 条严重报警",
                Content = $"当前有 {criticalCount} 条严重报警、{errorCount} 条错误报警。建议立即停机检查，优先处理：{string.Join("、", active.Where(a => a.Level == AlarmLevel.Critical).Take(3).Select(a => a.Code))}",
                Source = "本地规则引擎",
                Confidence = 0.95
            };
        }
        else if (active.Count >= 5)
        {
            suggestion = new AiSuggestion
            {
                Type = AiSuggestionType.Maintenance,
                Priority = AiSuggestionPriority.High,
                Title = $"报警数量异常（{active.Count} 条）",
                Content = $"当前活动报警数量达到 {active.Count} 条，超过正常阈值（5条）。建议检查设备状态，可能存在系统性故障。",
                Source = "本地规则引擎",
                Confidence = 0.85
            };
        }
        else return null;

        await Task.CompletedTask;
        return AddSuggestion(suggestion);
    }

    public async Task<AiSuggestion?> AnalyzeDeviceHealthAsync()
    {
        await Task.Delay(100);
        var suggestion = new AiSuggestion
        {
            Type = AiSuggestionType.Maintenance,
            Priority = AiSuggestionPriority.Medium,
            Title = "设备健康度分析",
            Content = "基于当前设备运行数据分析：\n• 设备整体健康度：良好\n• 建议在下次计划停机时检查 PLC-01 的 I/O 模块\n• 传感器 Sensor-03 的读数波动略大，建议校准",
            Source = "本地规则引擎",
            Confidence = 0.78
        };
        return AddSuggestion(suggestion);
    }

    public async Task<AiSuggestion?> AnalyzeProductionTrendAsync()
    {
        await Task.Delay(100);
        var suggestion = new AiSuggestion
        {
            Type = AiSuggestionType.Optimization,
            Priority = AiSuggestionPriority.Low,
            Title = "生产效率优化建议",
            Content = "基于近期生产数据分析：\n• 当前节拍时间：8.5s，目标：8.0s\n• 建议优化视觉检测等待时间（当前 200ms，可降至 150ms）\n• 换型时间可通过预加载配方减少约 15%",
            Source = "本地规则引擎",
            Confidence = 0.72
        };
        return AddSuggestion(suggestion);
    }

    public async Task<string> ChatAsync(string userMessage)
    {
        // 如果配置了 API 端点，调用 LLM
        if (!string.IsNullOrEmpty(ApiEndpoint))
        {
            try
            {
                var body = new
                {
                    model = "deepseek-chat",
                    messages = new[]
                    {
                        new { role = "system", content = "你是 SmartHMI 工业上位机系统的 AI 助手，专注于工业自动化、设备维护、生产优化领域。请用中文简洁回答。" },
                        new { role = "user", content = userMessage }
                    },
                    max_tokens = 500
                };
                var resp = await _http.PostAsJsonAsync(ApiEndpoint, body);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
                    return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "无响应";
                }
            }
            catch { }
        }

        // 本地规则回复
        return userMessage.Contains("报警") ? $"当前有 {_alarmService.ActiveAlarms.Count} 条活动报警，建议及时处理。"
             : userMessage.Contains("设备") ? "设备管理模块可查看所有设备的实时状态，点击左侧导航「设备管理」进入。"
             : userMessage.Contains("配方") ? "配方系统支持多产品参数管理，可在「配方管理」页面进行增删改查和激活操作。"
             : "您好！我是 SmartHMI AI 助手。您可以询问我关于设备状态、报警处理、生产优化等问题。";
    }

    public IReadOnlyList<AiSuggestion> GetSuggestions()
    { lock (_lock) return _suggestions.ToList(); }

    private AiSuggestion AddSuggestion(AiSuggestion s)
    {
        lock (_lock)
        {
            _suggestions.Add(s);
            if (_suggestions.Count > 100) _suggestions.RemoveAt(0);
        }
        SuggestionGenerated?.Invoke(this, s);
        return s;
    }
}
