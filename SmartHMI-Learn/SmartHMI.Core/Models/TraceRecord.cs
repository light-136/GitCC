namespace SmartHMI.Core.Models;

public enum TraceEventType { Start, StepComplete, Inspection, Alarm, Complete, Reject }

public class TraceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string WorkorderId { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string ProductType { get; set; } = "";
    public TraceEventType EventType { get; set; }
    public string StepName { get; set; } = "";
    public string Result { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string StationId { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public Dictionary<string, string> Data { get; set; } = new();
}
