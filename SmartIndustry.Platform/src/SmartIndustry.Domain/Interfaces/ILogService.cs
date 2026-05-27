// ============================================================
// 文件：ILogService.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义平台日志服务接口（结构化日志记录和查询）
// 设计思路：
//   ILogService 是平台统一的日志记录契约，与 Microsoft.Extensions.Logging
//   的区别在于：
//     1. 额外提供日志查询能力（GetRecentLogs、ExportLogsAsync）
//        标准 ILogger 只写不读，无法支持 UI 日志面板实时显示
//     2. 增加业务上下文参数（source 模块来源，correlationId 关联 ID）
//        便于追踪跨模块的业务流程
//   实现策略：ILogService 可以内部委托给 ILogger（Serilog/NLog），
//   同时额外维护一个内存或数据库日志缓存用于查询。
//   日志等级映射与 LogLevel 枚举对应，保证跨系统日志语义一致。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 日志条目数据类（查询结果的结构化表示）。
    /// </summary>
    public sealed class LogEntry
    {
        /// <summary>日志等级</summary>
        public LogLevel Level { get; init; }

        /// <summary>日志时间（UTC）</summary>
        public DateTime Timestamp { get; init; }

        /// <summary>来源模块名称</summary>
        public string Source { get; init; } = string.Empty;

        /// <summary>日志消息</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>异常信息（无异常时为 null）</summary>
        public string? ExceptionDetail { get; init; }

        /// <summary>关联 ID（用于追踪同一业务流程的多条日志，可为 null）</summary>
        public string? CorrelationId { get; init; }
    }

    /// <summary>
    /// 平台日志服务接口。
    /// 提供结构化日志写入和历史日志查询能力，支持 UI 日志面板实时显示。
    /// </summary>
    public interface ILogService
    {
        // ----------------------------------------------------------------
        // 日志写入方法（各等级快捷方法）
        // ----------------------------------------------------------------

        /// <summary>
        /// 记录调试日志（开发阶段细粒度追踪，生产环境默认关闭）。
        /// </summary>
        /// <param name="source">来源模块名称</param>
        /// <param name="message">日志消息</param>
        /// <param name="correlationId">关联 ID（可为 null）</param>
        void Debug(string source, string message, string? correlationId = null);

        /// <summary>
        /// 记录信息日志（正常业务流程的关键节点，始终启用）。
        /// </summary>
        void Info(string source, string message, string? correlationId = null);

        /// <summary>
        /// 记录警告日志（非预期但不影响主流程的情况）。
        /// </summary>
        void Warning(string source, string message, string? correlationId = null);

        /// <summary>
        /// 记录错误日志（影响功能的异常，需要包含完整异常信息）。
        /// </summary>
        /// <param name="source">来源模块名称</param>
        /// <param name="message">错误描述</param>
        /// <param name="exception">关联的异常对象（可为 null）</param>
        /// <param name="correlationId">关联 ID</param>
        void Error(string source, string message, Exception? exception = null, string? correlationId = null);

        /// <summary>
        /// 记录致命日志（导致应用崩溃或数据损坏的极端错误）。
        /// </summary>
        void Fatal(string source, string message, Exception? exception = null, string? correlationId = null);

        // ----------------------------------------------------------------
        // 日志查询
        // ----------------------------------------------------------------

        /// <summary>
        /// 获取最近 N 条日志（用于 UI 日志面板的初始加载）。
        /// </summary>
        /// <param name="count">获取条数（默认 100）</param>
        /// <param name="minLevel">最低日志等级过滤（默认 Info，过滤掉 Debug）</param>
        IReadOnlyList<LogEntry> GetRecentLogs(int count = 100, LogLevel minLevel = LogLevel.Info);

        // ----------------------------------------------------------------
        // 日志导出
        // ----------------------------------------------------------------

        /// <summary>
        /// 将指定时间范围内的日志导出到文件（CSV 或 JSON 格式）。
        /// </summary>
        /// <param name="startTime">导出起始时间（UTC）</param>
        /// <param name="endTime">导出结束时间（UTC）</param>
        /// <param name="outputFilePath">导出文件路径（扩展名决定格式：.csv 或 .json）</param>
        /// <param name="minLevel">最低等级过滤</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task ExportLogsAsync(
            DateTime startTime,
            DateTime endTime,
            string outputFilePath,
            LogLevel minLevel = LogLevel.Debug,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 实时日志订阅（UI 日志面板实时刷新）
        // ----------------------------------------------------------------

        /// <summary>
        /// 订阅新日志条目（实时推送，UI 日志面板用于实时追加显示）。
        /// </summary>
        /// <param name="minLevel">订阅的最低日志等级</param>
        /// <param name="handler">新日志到达时的回调</param>
        /// <returns>订阅令牌（Dispose 时取消订阅）</returns>
        IDisposable SubscribeToNewLogs(LogLevel minLevel, Action<LogEntry> handler);
    }
}
