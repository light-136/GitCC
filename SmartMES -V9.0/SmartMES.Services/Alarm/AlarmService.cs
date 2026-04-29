using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Services.Alarm
{
    /// <summary>
    /// 报警服务实现。
    /// 负责管理系统中所有报警的完整生命周期：触发 → 显示 → 确认 → 清除。
    /// 线程安全：所有集合操作均通过 _lock 加锁保护。
    /// </summary>
    public class AlarmService : IAlarmService
    {
        private readonly List<AlarmRecord> _alarms = new List<AlarmRecord>();
        private readonly ILoggingService _logger;
        private readonly object _lock = new object();

        /// <summary>当前报警列表（只读快照，防止外部直接修改集合）</summary>
        public IReadOnlyList<AlarmRecord> Alarms
        {
            get { lock (_lock) { return _alarms.ToList().AsReadOnly(); } }
        }

        /// <summary>报警触发事件，UI层订阅后可实时显示新报警</summary>
        public event EventHandler<AlarmRecord>? AlarmTriggered;

        /// <summary>报警确认事件，报警状态从 Active 变为 Acknowledged 时触发</summary>
        public event EventHandler<AlarmRecord>? AlarmAcknowledged;

        /// <summary>
        /// 构造报警服务，注入日志服务用于记录报警操作历史。
        /// </summary>
        /// <param name="logger">日志服务接口</param>
        public AlarmService(ILoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 触发新报警。
        /// 创建报警记录并加入列表，同时写入日志系统，再通知UI层更新报警列表。
        /// </summary>
        /// <param name="code">报警代码（例如：E001）</param>
        /// <param name="message">报警描述信息</param>
        /// <param name="level">报警级别（Critical/Warning/Info）</param>
        public void TriggerAlarm(string code, string message, AlarmLevel level)
        {
            var alarm = new AlarmRecord
            {
                Code = code,
                Message = message,
                Level = level,
                Status = AlarmStatus.Active,
                TriggeredAt = DateTime.Now
            };

            lock (_lock)
            {
                _alarms.Add(alarm);
            }

            // 将报警级别转换为可读中文，写入日志系统留存
            var logLevel = level == AlarmLevel.Critical
                ? "严重" : level == AlarmLevel.Warning ? "警告" : "提示";
            _logger.LogWarning($"[报警] [{logLevel}] {code}: {message}", "AlarmSystem");

            // 通知订阅者（UI层更新报警列表）
            AlarmTriggered?.Invoke(this, alarm);
        }

        /// <summary>
        /// 确认报警。
        /// 将报警状态从 Active 改为 Acknowledged，并记录确认时间戳。
        /// </summary>
        /// <param name="alarmId">要确认的报警唯一标识</param>
        public void AcknowledgeAlarm(Guid alarmId)
        {
            AlarmRecord? alarm;
            lock (_lock)
            {
                alarm = _alarms.FirstOrDefault(a => a.Id == alarmId);
                if (alarm == null) return;

                alarm.Status = AlarmStatus.Acknowledged;
                alarm.AcknowledgedAt = DateTime.Now;
            }

            _logger.LogInfo($"[报警确认] {alarm.Code}: {alarm.Message}", "AlarmSystem");
            AlarmAcknowledged?.Invoke(this, alarm);
        }

        /// <summary>
        /// 清除所有已确认的报警。
        /// 保留尚未确认的 Active 报警，避免误清除未处理的告警。
        /// </summary>
        public void ClearAcknowledgedAlarms()
        {
            lock (_lock)
            {
                _alarms.RemoveAll(a => a.Status == AlarmStatus.Acknowledged);
            }
            _logger.LogInfo("已清除所有已确认报警", "AlarmSystem");
        }
    }
}
