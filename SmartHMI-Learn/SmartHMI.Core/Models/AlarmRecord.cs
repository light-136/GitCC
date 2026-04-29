namespace SmartHMI.Core.Models;

public enum AlarmLevel { Info, Warning, Error, Critical }

public class AlarmRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public AlarmLevel Level { get; init; }
    public string Source { get; init; } = "";
    public DateTime TriggeredAt { get; init; } = DateTime.Now;
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ClearedAt { get; set; }
    public bool IsActive => ClearedAt == null;
    public bool IsAcknowledged => AcknowledgedAt != null;
}
