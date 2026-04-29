namespace SmartMES.Core.Models
{
    /// <summary>
    /// 报警级别枚举
    /// 按严重程度分级，影响显示颜色和处理优先级
    /// </summary>
    public enum AlarmLevel
    {
        /// <summary>普通提示，无需立即处理</summary>
        Info,
        /// <summary>警告，需要关注</summary>
        Warning,
        /// <summary>严重报警，需要立即处理</summary>
        Critical
    }

    /// <summary>
    /// 报警状态枚举
    /// </summary>
    public enum AlarmStatus
    {
        /// <summary>活跃中，尚未确认</summary>
        Active,
        /// <summary>已确认，等待消除</summary>
        Acknowledged,
        /// <summary>已消除</summary>
        Cleared
    }

    /// <summary>
    /// 报警记录模型
    /// 记录系统中每一次报警事件的完整信息
    /// </summary>
    public class AlarmRecord
    {
        /// <summary>报警唯一ID</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>报警代码（如 ALM-001）</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>报警消息描述</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>报警级别</summary>
        public AlarmLevel Level { get; set; }

        /// <summary>报警状态</summary>
        public AlarmStatus Status { get; set; } = AlarmStatus.Active;

        /// <summary>报警触发时间</summary>
        public DateTime TriggeredAt { get; set; } = DateTime.Now;

        /// <summary>确认时间（未确认时为null）</summary>
        public DateTime? AcknowledgedAt { get; set; }

        /// <summary>确认操作员</summary>
        public string? AcknowledgedBy { get; set; }

        /// <summary>报警来源设备</summary>
        public string Source { get; set; } = "System";
    }
}
