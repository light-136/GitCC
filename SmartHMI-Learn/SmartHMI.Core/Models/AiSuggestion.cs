namespace SmartHMI.Core.Models;

public enum AiSuggestionType { Maintenance, Optimization, Alarm, Quality, Process }
public enum AiSuggestionPriority { Low, Medium, High, Critical }

public class AiSuggestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AiSuggestionType Type { get; set; }
    public AiSuggestionPriority Priority { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Source { get; set; } = "";
    public double Confidence { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public bool IsRead { get; set; }
    public bool IsActioned { get; set; }
}
