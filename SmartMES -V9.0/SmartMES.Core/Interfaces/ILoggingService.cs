using SmartMES.Core.Models;

namespace SmartMES.Core.Interfaces
{
    /// <summary>
    /// 日志服务接口
    /// 通过接口隔离日志实现，便于替换日志框架（如NLog、Serilog）
    /// </summary>
    public interface ILoggingService
    {
        /// <summary>记录信息日志</summary>
        void LogInfo(string message, string source = "System");

        /// <summary>记录警告日志</summary>
        void LogWarning(string message, string source = "System");

        /// <summary>记录错误日志</summary>
        void LogError(string message, string source = "System");

        /// <summary>记录通信日志</summary>
        void LogCommunication(string message, string source = "Communication");

        /// <summary>获取所有日志记录（用于UI绑定）</summary>
        IReadOnlyList<LogEntry> GetLogs();

        /// <summary>日志新增事件（用于实时推送到UI）</summary>
        event EventHandler<LogEntry>? LogAdded;
    }
}
