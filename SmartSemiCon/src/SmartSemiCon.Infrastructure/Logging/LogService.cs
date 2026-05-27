// ============================================================
// 文件：LogService.cs
// 用途：日志服务实现
// 设计思路：
//   双输出日志系统：
//   1. Serilog → 文件日志（持久化，用于故障分析）
//   2. 内存环形缓冲区 → UI日志面板（实时展示）
//
//   使用 ConcurrentQueue 作为环形缓冲区，限制最大容量防止内存溢出。
//   通过 LogAdded 事件通知 UI 有新日志写入。
//
//   日志分类思路：
//   - source 标识日志来源模块（如 "运动控制"、"视觉"、"SECS/GEM"）
//   - traceId 关联同一操作的多条日志（如一次完整的运动过程）
// ============================================================

using System.Collections.Concurrent;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;
using Microsoft.Extensions.Logging;

namespace SmartSemiCon.Infrastructure.Logging
{
    /// <summary>
    /// 日志服务 — 同时输出到文件和UI面板。
    /// 注册为单例到DI容器。
    /// </summary>
    public class LogService : ILogService
    {
        private readonly ILogger<LogService> _logger;

        // 环形缓冲区 — 保存最近的日志条目供UI展示
        private readonly ConcurrentQueue<LogEntry> _recentLogs = new();

        // 环形缓冲区最大容量
        private const int MaxLogCount = 5000;

        /// <summary>新日志写入事件 — UI日志面板订阅此事件实时更新</summary>
        public event EventHandler<LogEntry>? LogAdded;

        public LogService(ILogger<LogService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 写入日志 — 同时写入Serilog和内存缓冲区。
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="source">来源模块名称</param>
        /// <param name="message">日志内容</param>
        /// <param name="traceId">追踪ID（可选）</param>
        public void Log(Domain.Enums.LogLevel level, string source, string message, string? traceId = null)
        {
            // 构造日志条目
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Source = source,
                Message = message,
                TraceId = traceId
            };

            // 写入Serilog（持久化到文件）
            var formattedMessage = $"[{source}] {message}";
            switch (level)
            {
                case Domain.Enums.LogLevel.Debug:
                    _logger.LogDebug(formattedMessage);
                    break;
                case Domain.Enums.LogLevel.Info:
                    _logger.LogInformation(formattedMessage);
                    break;
                case Domain.Enums.LogLevel.Warning:
                    _logger.LogWarning(formattedMessage);
                    break;
                case Domain.Enums.LogLevel.Error:
                    _logger.LogError(formattedMessage);
                    break;
                case Domain.Enums.LogLevel.Fatal:
                    _logger.LogCritical(formattedMessage);
                    break;
            }

            // 写入内存缓冲区（环形队列，超过上限时移除最早的）
            _recentLogs.Enqueue(entry);
            while (_recentLogs.Count > MaxLogCount)
            {
                _recentLogs.TryDequeue(out _);
            }

            // 通知UI有新日志
            LogAdded?.Invoke(this, entry);
        }

        /// <summary>
        /// 获取最近的日志条目。
        /// </summary>
        public IReadOnlyList<LogEntry> GetRecentLogs(int count = 100)
        {
            return _recentLogs.TakeLast(count).ToList().AsReadOnly();
        }
    }
}
