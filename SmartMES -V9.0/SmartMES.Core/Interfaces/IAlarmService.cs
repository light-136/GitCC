using SmartMES.Core.Models;

namespace SmartMES.Core.Interfaces
{
    /// <summary>
    /// 报警服务接口
    /// 负责报警触发、确认、查询等操作
    /// </summary>
    public interface IAlarmService
    {
        /// <summary>当前所有报警列表</summary>
        IReadOnlyList<AlarmRecord> Alarms { get; }

        /// <summary>
        /// 触发一条新报警
        /// </summary>
        /// <param name="code">报警代码</param>
        /// <param name="message">报警消息</param>
        /// <param name="level">报警级别</param>
        void TriggerAlarm(string code, string message, AlarmLevel level);

        /// <summary>
        /// 确认指定报警
        /// </summary>
        /// <param name="alarmId">报警记录ID</param>
        void AcknowledgeAlarm(Guid alarmId);

        /// <summary>清除所有已确认报警</summary>
        void ClearAcknowledgedAlarms();

        /// <summary>报警触发事件</summary>
        event EventHandler<AlarmRecord>? AlarmTriggered;

        /// <summary>报警确认事件</summary>
        event EventHandler<AlarmRecord>? AlarmAcknowledged;
    }
}
