// ============================================================
// 文件：SerilogService.cs
// 层次：基础设施层 (Infrastructure Layer) — 日志服务实现
// 职责：
//   实现 SmartIndustry.Domain.Interfaces.ILogService 接口，提供：
//   - 双通道日志：Serilog 文件日志（JSON 格式，按日期滚动）+ 内存环形缓冲区
//   - 内存缓冲区：最近 N 条日志（默认1000条）供 UI 实时读取，无需读文件
//   - 日志级别动态配置（运行时修改，无需重启）
//   - ExportLogsAsync：按时间范围导出日志到文件
//   - SubscribeToNewLogs：实时推送新日志给 UI 面板订阅者
// 设计思路：
//   内存环形缓冲区使用 ConcurrentQueue<LogEntry> + Interlocked 实现无锁写入。
//   订阅者列表用 ConcurrentDictionary 管理，Dispose 令牌取消订阅。
//   Serilog 文件日志使用 JSON 格式便于日志聚合（ELK Stack 等）。
//   文件按日期滚动：每天新建一个日志文件，防止单文件过大。
//   注意：Domain 层 ILogService 中内置了 LogEntry 类（SmartIndustry.Domain.Interfaces.LogEntry），
//   此实现使用该类型，不使用 Entity 层的 LogEntry（避免循环依赖）。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using Serilog;
using Serilog.Core;
using Serilog.Events;
using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.Interfaces;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

// 使用 Domain 层定义的 LogEntry（在 SmartIndustry.Domain.Interfaces 命名空间）
using DomainLogEntry = SmartIndustry.Domain.Interfaces.LogEntry;
using DomainLogLevel = SmartIndustry.Domain.Enums.LogLevel;

namespace SmartIndustry.Infrastructure.Logging
{
    /// <summary>
    /// Serilog 日志服务实现，实现 Domain 层 ILogService 接口。
    /// 双通道：文件日志（JSON 格式，按日期滚动）+ 内存环形缓冲区（供 UI 读取）。
    /// </summary>
    public class SerilogService : ILogService, IDisposable
    {
        // ----------------------------------------------------------------
        // 内部字段
        // ----------------------------------------------------------------

        /// <summary>Serilog 日志记录器实例（线程安全）</summary>
        private readonly Serilog.ILogger _logger;

        /// <summary>
        /// Serilog 动态日志级别开关（支持运行时修改，立即生效）
        /// </summary>
        private readonly LoggingLevelSwitch _levelSwitch;

        /// <summary>
        /// 内存环形缓冲区（线程安全 FIFO 队列）。
        /// 超出 _maxBufferSize 时移除最旧条目（FIFO 淘汰）。
        /// </summary>
        private readonly ConcurrentQueue<DomainLogEntry> _logBuffer = new();

        /// <summary>环形缓冲区最大容量（默认1000条）</summary>
        private readonly int _maxBufferSize;

        /// <summary>当前缓冲区条目数（Interlocked 原子操作）</summary>
        private int _bufferCount = 0;

        // ----------------------------------------------------------------
        // 订阅者管理（实时推送）
        // ----------------------------------------------------------------

        /// <summary>
        /// 活跃订阅者字典：SubscriptionId -> (最低等级, 回调)
        /// 新日志写入时遍历订阅者并按等级过滤分发
        /// </summary>
        private readonly ConcurrentDictionary<Guid, (DomainLogLevel MinLevel, Action<DomainLogEntry> Handler)>
            _subscribers = new();

        // ================================================================
        // 构造函数：初始化 Serilog 和内存缓冲区
        // ================================================================

        /// <summary>
        /// 初始化日志服务。
        /// </summary>
        /// <param name="logDirectory">日志文件存储目录（null=应用目录/Logs）</param>
        /// <param name="maxBufferSize">内存缓冲区最大条目数（默认1000）</param>
        /// <param name="initialLevel">初始最低日志级别（默认 Info）</param>
        public SerilogService(
            string? logDirectory = null,
            int maxBufferSize = 1000,
            DomainLogLevel initialLevel = DomainLogLevel.Info)
        {
            _maxBufferSize = maxBufferSize > 0 ? maxBufferSize : 1000;

            var logDir = logDirectory ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Logs");

            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            // 创建动态级别开关
            _levelSwitch = new LoggingLevelSwitch(MapToSerilogLevel(initialLevel));

            // 配置 Serilog：按日期滚动的 JSON 格式文件日志
            _logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(_levelSwitch)
                .WriteTo.File(
                    path: Path.Combine(logDir, "smartindustry-.log"),
                    formatter: new Serilog.Formatting.Json.JsonFormatter(),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    fileSizeLimitBytes: 100 * 1024 * 1024,
                    shared: true)
                .CreateLogger();
        }

        // ================================================================
        // ILogService 实现：写日志方法（匹配 Domain 层接口签名）
        // ================================================================

        /// <summary>记录调试级别日志</summary>
        public void Debug(string source, string message, string? correlationId = null)
            => WriteLog(DomainLogLevel.Debug, source, message, null, correlationId);

        /// <summary>记录信息级别日志</summary>
        public void Info(string source, string message, string? correlationId = null)
            => WriteLog(DomainLogLevel.Info, source, message, null, correlationId);

        /// <summary>记录警告级别日志</summary>
        public void Warning(string source, string message, string? correlationId = null)
            => WriteLog(DomainLogLevel.Warning, source, message, null, correlationId);

        /// <summary>记录错误级别日志（附带异常信息）</summary>
        public void Error(string source, string message, Exception? exception = null, string? correlationId = null)
            => WriteLog(DomainLogLevel.Error, source, message, exception, correlationId);

        /// <summary>记录致命级别日志</summary>
        public void Fatal(string source, string message, Exception? exception = null, string? correlationId = null)
            => WriteLog(DomainLogLevel.Fatal, source, message, exception, correlationId);

        // ================================================================
        // ILogService 实现：日志查询
        // ================================================================

        /// <summary>
        /// 获取内存缓冲区中最近 N 条日志（快速读取，不访问文件）。
        /// 返回按时间降序排列的日志（最新在最前）。
        /// </summary>
        public IReadOnlyList<DomainLogEntry> GetRecentLogs(int count = 100, DomainLogLevel minLevel = DomainLogLevel.Info)
        {
            return _logBuffer
                .Where(e => e.Level >= minLevel)
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToList();
        }

        // ================================================================
        // ILogService 实现：日志导出
        // ================================================================

        /// <summary>
        /// 将指定时间范围内的内存缓冲日志导出到文件。
        /// 支持 JSON（.json 扩展名）和 CSV（其他扩展名）格式。
        /// </summary>
        public async Task ExportLogsAsync(
            DateTime startTime,
            DateTime endTime,
            string outputFilePath,
            DomainLogLevel minLevel = DomainLogLevel.Debug,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(outputFilePath))
                throw new ArgumentException("导出路径不能为空", nameof(outputFilePath));

            var logs = _logBuffer
                .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime && e.Level >= minLevel)
                .OrderBy(e => e.Timestamp)
                .ToList();

            var ext = Path.GetExtension(outputFilePath).ToLowerInvariant();

            if (ext == ".json")
            {
                var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await File.WriteAllTextAsync(outputFilePath, json, Encoding.UTF8, cancellationToken);
            }
            else
            {
                // CSV 格式导出
                var sb = new StringBuilder();
                sb.AppendLine("时间戳,级别,来源,消息,异常详情,关联ID");
                foreach (var log in logs)
                {
                    sb.AppendLine(string.Join(",",
                        $"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss.fff}\"",
                        $"\"{log.Level}\"",
                        $"\"{EscapeCsv(log.Source)}\"",
                        $"\"{EscapeCsv(log.Message)}\"",
                        $"\"{EscapeCsv(log.ExceptionDetail)}\"",
                        $"\"{EscapeCsv(log.CorrelationId)}\""
                    ));
                }
                await File.WriteAllTextAsync(outputFilePath, sb.ToString(), Encoding.UTF8, cancellationToken);
            }
        }

        // ================================================================
        // ILogService 实现：实时订阅
        // ================================================================

        /// <summary>
        /// 订阅新日志条目（实时推送给 UI 日志面板）。
        /// 返回 IDisposable 令牌，Dispose 时自动取消订阅。
        /// </summary>
        public IDisposable SubscribeToNewLogs(DomainLogLevel minLevel, Action<DomainLogEntry> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var subscriptionId = Guid.NewGuid();
            _subscribers[subscriptionId] = (minLevel, handler);

            // 返回令牌，Dispose 时从订阅字典中移除
            return new SubscriptionToken(() => _subscribers.TryRemove(subscriptionId, out _));
        }

        // ================================================================
        // 额外的动态级别控制方法（非接口，供内部使用）
        // ================================================================

        /// <summary>动态修改最低日志级别（立即生效，无需重启）</summary>
        public void SetMinLevel(DomainLogLevel level)
            => _levelSwitch.MinimumLevel = MapToSerilogLevel(level);

        /// <summary>获取当前最低日志级别</summary>
        public DomainLogLevel GetMinLevel()
            => MapFromSerilogLevel(_levelSwitch.MinimumLevel);

        // ================================================================
        // 私有工具方法
        // ================================================================

        /// <summary>
        /// 统一日志写入入口：同时写 Serilog（文件）和内存缓冲区，通知订阅者
        /// </summary>
        private void WriteLog(DomainLogLevel level, string source, string message,
            Exception? exception, string? correlationId)
        {
            // 1. 写入 Serilog 文件日志
            var serilogLevel = MapToSerilogLevel(level);
            if (exception != null)
                _logger.Write(serilogLevel, exception, "[{Source}] {Message} CorrelationId={CorrelationId}",
                    source, message, correlationId);
            else
                _logger.Write(serilogLevel, "[{Source}] {Message} CorrelationId={CorrelationId}",
                    source, message, correlationId);

            // 2. 写入内存缓冲区
            var entry = new DomainLogEntry
            {
                Level = level,
                Timestamp = DateTime.Now,  // 内存缓冲区使用本地时间便于 UI 显示
                Source = source,
                Message = message,
                ExceptionDetail = exception != null
                    ? $"{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}"
                    : null,
                CorrelationId = correlationId
            };

            _logBuffer.Enqueue(entry);

            // 维护缓冲区大小上限（原子递增，超出时出队最旧条目）
            var count = Interlocked.Increment(ref _bufferCount);
            if (count > _maxBufferSize)
            {
                if (_logBuffer.TryDequeue(out _))
                    Interlocked.Decrement(ref _bufferCount);
            }

            // 3. 通知所有活跃订阅者（按等级过滤）
            foreach (var (id, (minLevel, handler)) in _subscribers)
            {
                if (level >= minLevel)
                {
                    try { handler(entry); }
                    catch
                    {
                        // 订阅者异常隔离：不影响日志写入
                        _subscribers.TryRemove(id, out _);
                    }
                }
            }
        }

        private static string? EscapeCsv(string? value) => value?.Replace("\"", "\"\"");

        private static LogEventLevel MapToSerilogLevel(DomainLogLevel level) => level switch
        {
            DomainLogLevel.Debug => LogEventLevel.Debug,
            DomainLogLevel.Info => LogEventLevel.Information,
            DomainLogLevel.Warning => LogEventLevel.Warning,
            DomainLogLevel.Error => LogEventLevel.Error,
            DomainLogLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

        private static DomainLogLevel MapFromSerilogLevel(LogEventLevel level) => level switch
        {
            LogEventLevel.Debug => DomainLogLevel.Debug,
            LogEventLevel.Information => DomainLogLevel.Info,
            LogEventLevel.Warning => DomainLogLevel.Warning,
            LogEventLevel.Error => DomainLogLevel.Error,
            LogEventLevel.Fatal => DomainLogLevel.Fatal,
            _ => DomainLogLevel.Info
        };

        // ================================================================
        // 订阅令牌（内部类）
        // ================================================================

        private sealed class SubscriptionToken : IDisposable
        {
            private readonly Action _dispose;
            private bool _disposed;
            public SubscriptionToken(Action dispose) => _dispose = dispose;
            public void Dispose() { if (!_disposed) { _disposed = true; _dispose(); } }
        }

        // ================================================================
        // IDisposable
        // ================================================================

        public void Dispose()
        {
            if (_logger is IDisposable d) d.Dispose();
            _subscribers.Clear();
        }
    }
}
