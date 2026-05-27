// ============================================================
// 文件：AlarmManager.cs
// 用途：报警管理器
// 设计思路：
//   工业设备报警系统的核心要求：
//   1. 报警触发要快 — 不能因为报警处理而延迟设备响应
//   2. 报警记录要全 — 所有报警必须记录历史
//   3. 报警复位要安全 — 清除报警前确认故障已排除
//   4. 支持一键复位 — 操作员可快速清除所有报警恢复生产
//
//   报警代码规范（按模块分段）：
//   1000~1999：运动控制报警
//   2000~2999：视觉系统报警
//   3000~3999：通讯报警
//   4000~4999：安全报警
//   5000~5999：流程报警
// ============================================================

using System.Collections.Concurrent;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Events;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.Application.Alarm
{
    /// <summary>
    /// 报警管理器 — 管理设备所有报警的触发、清除和历史记录。
    /// </summary>
    public class AlarmManager : IAlarmService
    {
        private readonly ConcurrentDictionary<int, AlarmRecord> _activeAlarms = new();
        private readonly ConcurrentBag<AlarmRecord> _history = new();
        private readonly IEventBus _eventBus;
        private readonly ILogService _logService;

        /// <summary>当前活跃报警列表</summary>
        public IReadOnlyList<AlarmRecord> ActiveAlarms =>
            _activeAlarms.Values.ToList().AsReadOnly();

        /// <summary>报警触发事件</summary>
        public event EventHandler<AlarmRecord>? AlarmTriggered;

        /// <summary>报警清除事件</summary>
        public event EventHandler<int>? AlarmCleared;

        public AlarmManager(IEventBus eventBus, ILogService logService)
        {
            _eventBus = eventBus;
            _logService = logService;
        }

        /// <summary>
        /// 触发报警。
        /// </summary>
        public void TriggerAlarm(int alarmCode, AlarmLevel level, string message, string source)
        {
            // 同一报警代码不重复触发
            if (_activeAlarms.ContainsKey(alarmCode)) return;

            var record = new AlarmRecord
            {
                AlarmCode = alarmCode,
                Level = level,
                Message = message,
                Source = source,
                OccurredAt = DateTime.Now
            };

            _activeAlarms[alarmCode] = record;
            _history.Add(record);

            // 记录日志
            _logService.Log(
                level >= AlarmLevel.Heavy ? Domain.Enums.LogLevel.Error : Domain.Enums.LogLevel.Warning,
                source,
                $"报警触发 [{alarmCode}] {message}");

            // 发布事件
            _eventBus.Publish(new AlarmTriggeredEvent { Alarm = record, Source = source });
            AlarmTriggered?.Invoke(this, record);
        }

        /// <summary>
        /// 清除指定报警。
        /// </summary>
        public void ClearAlarm(int alarmCode, string clearedBy)
        {
            if (_activeAlarms.TryRemove(alarmCode, out var record))
            {
                record.ClearedAt = DateTime.Now;
                record.ClearedBy = clearedBy;

                _logService.Log(Domain.Enums.LogLevel.Info, "报警系统",
                    $"报警清除 [{alarmCode}] 操作员: {clearedBy}");

                _eventBus.Publish(new AlarmClearedEvent
                {
                    AlarmCode = alarmCode,
                    ClearedBy = clearedBy,
                    Source = "报警系统"
                });
                AlarmCleared?.Invoke(this, alarmCode);
            }
        }

        /// <summary>
        /// 一键清除所有报警。
        /// </summary>
        public void ClearAllAlarms(string clearedBy)
        {
            var codes = _activeAlarms.Keys.ToList();
            foreach (var code in codes)
            {
                ClearAlarm(code, clearedBy);
            }
        }

        /// <summary>
        /// 查询报警历史。
        /// </summary>
        public Task<List<AlarmRecord>> GetHistoryAsync(DateTime from, DateTime to)
        {
            var records = _history
                .Where(r => r.OccurredAt >= from && r.OccurredAt <= to)
                .OrderByDescending(r => r.OccurredAt)
                .ToList();

            return Task.FromResult(records);
        }
    }
}
