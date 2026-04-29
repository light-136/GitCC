namespace SmartMES.Core.Models
{
    /// <summary>
    /// 日志级别枚举
    /// 定义日志的严重程度，便于过滤和显示不同颜色
    /// </summary>
    public enum LogLevel
    {
        /// <summary>普通信息，正常操作记录</summary>
        Info,
        /// <summary>警告，需要关注但不影响运行</summary>
        Warning,
        /// <summary>错误，系统出现异常</summary>
        Error,
        /// <summary>通信日志，数据收发记录</summary>
        Communication
    }

    /// <summary>
    /// 日志条目模型
    /// 记录系统中发生的每一条日志信息
    /// </summary>
    public class LogEntry
    {
        /// <summary>日志唯一ID</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>日志时间</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>日志级别</summary>
        public LogLevel Level { get; set; }

        /// <summary>日志消息内容</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>日志来源模块</summary>
        public string Source { get; set; } = "System";

        /// <summary>
        /// 格式化显示字符串
        /// 用于日志文件写入
        /// </summary>
        public override string ToString()
            => $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] [{Source}] {Message}";
    }
}
