using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface IAiAssistantService
{
    bool IsEnabled { get; }
    string ApiEndpoint { get; set; }
    Task<AiSuggestion?> AnalyzeAlarmsAsync();
    Task<AiSuggestion?> AnalyzeDeviceHealthAsync();
    Task<AiSuggestion?> AnalyzeProductionTrendAsync();
    Task<string> ChatAsync(string userMessage);
    IReadOnlyList<AiSuggestion> GetSuggestions();
    event EventHandler<AiSuggestion>? SuggestionGenerated;
}
