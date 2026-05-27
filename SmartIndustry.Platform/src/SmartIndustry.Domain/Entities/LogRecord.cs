// ============================================================
// 文件：LogRecord.cs
// 层次：领域层 (Domain Layer) — 实体
// 职责：可被 EF Core 持久化的结构化日志记录实体（数据库版）。
//       与 SmartIndustry.Domain.Interfaces.LogEntry（内存 DTO）不同，
//       此类继承 BaseEntity，拥有 Guid 主键和完整审计字段，可映射到数据库表。
// 设计思路：
//   工业平台需要将关键日志持久化到数据库供历史查询和导出。
//   Interfaces.LogEntry 是内存缓冲区用的轻量 DTO（无主键，无审计字段）。
//   LogRecord 是数据库持久化版本，两者通过 SerilogService 的双写实现同步：
//     写入文件（Serilog）+ 写入内存 DTO（ILogService.LogEntry）
//     持久化关键日志（Error/Fatal级别）写入 LogRecord（可选）
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Entities
{
    /// <summary>
    /// 结构化日志记录实体，对应数据库表 LogEntries。
    /// 用于将关键日志持久化到数据库，支持跨会话的历史日志查询。
    /// </summary>
    public class LogRecord : BaseEntity
    {
        // ----------------------------------------------------------------
        // 日志基本属性
        // ----------------------------------------------------------------

        /// <summary>日志等级（Debug/Info/Warning/Error/Fatal）</summary>
        public LogLevel Level { get; set; } = LogLevel.Info;

        /// <summary>日志消息内容（核心文本，不含异常堆栈）</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>日志来源（类名、模块名，如 "AsyncTcpClient"、"AxisController"）</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>日志记录精确时间（本地时间，便于日志面板直接显示）</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        // ----------------------------------------------------------------
        // 异常信息（可选，仅 Error/Fatal 级别日志填充）
        // ----------------------------------------------------------------

        /// <summary>异常类型全名（null=非异常日志）</summary>
        public string? ExceptionType { get; set; }

        /// <summary>异常消息（null=无异常）</summary>
        public string? ExceptionMessage { get; set; }

        /// <summary>异常堆栈跟踪（null=无异常）</summary>
        public string? StackTrace { get; set; }

        // ----------------------------------------------------------------
        // 扩展属性
        // ----------------------------------------------------------------

        /// <summary>关联的操作用户名（null=系统自动操作）</summary>
        public string? Username { get; set; }

        /// <summary>关联 ID（用于追踪同一业务流程的多条日志，可为 null）</summary>
        public string? CorrelationId { get; set; }

        /// <summary>
        /// 扩展属性（JSON格式，存储结构化日志上下文字段）
        /// </summary>
        public string? Properties { get; set; }
    }
}
