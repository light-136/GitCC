using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Services;

public class LoggingService : ILoggingService
{
    private readonly List<LogEntry> _entries = new();
    private readonly Lock _lock = new();
    private const int MaxEntries = 2000;

    public event EventHandler<LogEntry>? EntryAdded;

    private void Add(LogLevel level, string message, string module, Exception? ex = null)
    {
        var entry = new LogEntry
        {
            Level = level,
            Message = message,
            Module = module,
            ExceptionDetail = ex?.ToString()
        };
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
        }
        EntryAdded?.Invoke(this, entry);
    }

    public void Info(string message, string module = "System") => Add(LogLevel.Info, message, module);
    public void Warning(string message, string module = "System") => Add(LogLevel.Warning, message, module);
    public void Error(string message, string module = "System", Exception? ex = null) => Add(LogLevel.Error, message, module, ex);
    public void Debug(string message, string module = "System") => Add(LogLevel.Debug, message, module);

    public IReadOnlyList<LogEntry> GetRecent(int count = 200)
    {
        lock (_lock) return _entries.TakeLast(count).ToList();
    }

    public IReadOnlyList<LogEntry> GetByModule(string module)
    {
        lock (_lock) return _entries.Where(e => e.Module == module).ToList();
    }
}
