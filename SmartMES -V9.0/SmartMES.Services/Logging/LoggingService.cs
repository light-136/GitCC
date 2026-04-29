using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Services.Logging
{
    /// <summary>
    /// 日志服务实现。
    /// 负责收集系统日志、通知 UI，并异步落盘到本地文件。
    /// </summary>
    public class LoggingService : ILoggingService
    {
        /// <summary>内存日志列表，供界面读取快照使用。</summary>
        private readonly List<LogEntry> _logs = new();

        /// <summary>文件写入锁，避免并发追加日志时互相覆盖。</summary>
        private readonly SemaphoreSlim _fileLock = new(1, 1);

        /// <summary>日志输出目录。</summary>
        private readonly string _logDirectory;

        /// <summary>内存中最多保留的日志条数。</summary>
        private const int MaxMemoryLogs = 1000;

        /// <summary>日志新增事件，用于 UI 实时刷新。</summary>
        public event EventHandler<LogEntry>? LogAdded;

        /// <summary>
        /// 创建日志服务并确保日志目录存在。
        /// </summary>
        public LoggingService(string logDirectory = "Logs")
        {
            _logDirectory = logDirectory;
            Directory.CreateDirectory(_logDirectory);
        }

        /// <summary>记录信息级日志。</summary>
        public void LogInfo(string message, string source = "System")
            => AddLog(message, LogLevel.Info, source);

        /// <summary>记录警告级日志。</summary>
        public void LogWarning(string message, string source = "System")
            => AddLog(message, LogLevel.Warning, source);

        /// <summary>记录错误级日志。</summary>
        public void LogError(string message, string source = "System")
            => AddLog(message, LogLevel.Error, source);

        /// <summary>记录通信级日志。</summary>
        public void LogCommunication(string message, string source = "Communication")
            => AddLog(message, LogLevel.Communication, source);

        /// <summary>
        /// 返回当前日志快照，避免外部直接修改内部集合。
        /// </summary>
        public IReadOnlyList<LogEntry> GetLogs()
        {
            lock (_logs)
            {
                return _logs.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// 将日志写入内存并触发异步文件落盘。
        /// </summary>
        private void AddLog(string message, LogLevel level, string source)
        {
            var entry = new LogEntry
            {
                Level = level,
                Message = message,
                Source = source,
                Timestamp = DateTime.Now
            };

            lock (_logs)
            {
                _logs.Add(entry);
                if (_logs.Count > MaxMemoryLogs)
                    _logs.RemoveAt(0);
            }

            LogAdded?.Invoke(this, entry);
            _ = WriteToFileAsync(entry);
        }

        /// <summary>
        /// 以“按天分文件”的方式将日志追加写入本地。
        /// </summary>
        private async Task WriteToFileAsync(LogEntry entry)
        {
            var fileName = Path.Combine(_logDirectory, $"log_{DateTime.Now:yyyy-MM-dd}.txt");

            await _fileLock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(fileName, entry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[日志写入失败] {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }
    }
}
