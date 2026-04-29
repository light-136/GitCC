namespace SmartHMI.Core.Models;

public enum LogLevel { Debug, Info, Warning, Error }

public class LogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; }
    public string Module { get; init; } = "System";
    public string Message { get; init; } = "";
    public string? ExceptionDetail { get; init; }
}
