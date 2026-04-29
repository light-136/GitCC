using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface ILoggingService
{
    void Info(string message, string module = "System");
    void Warning(string message, string module = "System");
    void Error(string message, string module = "System", Exception? ex = null);
    void Debug(string message, string module = "System");
    IReadOnlyList<LogEntry> GetRecent(int count = 200);
    IReadOnlyList<LogEntry> GetByModule(string module);
}
