// ============================================================
// 文件：AlarmService.cs
// 层次：应用层 (Application Layer) — 报警服务
// 职责：实现 IAlarmService 接口
// ============================================================

using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.Events;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Interfaces.Repositories;
using System.Collections.Concurrent;

namespace SmartIndustry.Application.Alarm
{
    /// <summary>
    /// 报警定义（预配置）
    /// </summary>
    public class AlarmDefinition
    {
        public string AlarmCode { get; init; } = string.Empty;
        public AlarmLevel DefaultLevel { get; init; }
        public AlarmCategory DefaultCategory { get; init; }
        public string DescriptionTemplate { get; init; } = string.Empty;
    }

    /// <summary>
    /// 报警服务实现
    /// </summary>
    public class AlarmService : IAlarmService
    {
        private readonly IAlarmRepository _alarmRepository;
        private readonly IEventBus _eventBus;
        private readonly ILogService _logService;

        private readonly ConcurrentDictionary<string, AlarmDefinition> _definitions = new();
        private readonly ConcurrentDictionary<string, AlarmRecord> _activeAlarms = new();

        public AlarmService(
            IAlarmRepository alarmRepository,
            IEventBus eventBus,
            ILogService logService)
        {
            _alarmRepository = alarmRepository ?? throw new ArgumentNullException(nameof(alarmRepository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        public void RegisterAlarmDefinition(string alarmCode, AlarmLevel defaultLevel,
            AlarmCategory defaultCategory, string descriptionTemplate)
        {
            _definitions[alarmCode] = new AlarmDefinition
            {
                AlarmCode = alarmCode,
                DefaultLevel = defaultLevel,
                DefaultCategory = defaultCategory,
                DescriptionTemplate = descriptionTemplate
            };
        }

        public async Task<AlarmRecord> TriggerAlarm(string alarmCode, string source, string message,
            string? detail = null, AlarmLevel? level = null, AlarmCategory? category = null)
        {
            // 如果该报警码已有活跃报警，不重复触发
            if (_activeAlarms.TryGetValue(alarmCode, out var existing))
                return existing;

            _definitions.TryGetValue(alarmCode, out var definition);

            var alarm = new AlarmRecord
            {
                AlarmCode = alarmCode,
                Title = definition?.DescriptionTemplate ?? alarmCode,
                Level = level ?? definition?.DefaultLevel ?? AlarmLevel.Warning,
                Category = category ?? definition?.DefaultCategory ?? AlarmCategory.System,
                Message = message,
                Source = source,
                IsActive = true,
                TriggeredAt = DateTime.Now,
                AdditionalData = detail
            };

            await _alarmRepository.AddAsync(alarm);
            await _alarmRepository.SaveChangesAsync();

            _activeAlarms[alarmCode] = alarm;

            await _eventBus.PublishAsync(new AlarmTriggeredEvent(alarm));

            _logService.Warning("AlarmService",
                $"报警触发：[{alarm.Level}] {alarm.AlarmCode} - {alarm.Message}");

            return alarm;
        }

        public async Task AcknowledgeAlarm(Guid alarmId, string acknowledgedBy)
        {
            var alarm = await _alarmRepository.GetByIdAsync(alarmId);
            if (alarm == null) return;

            alarm.Acknowledge(acknowledgedBy);
            _alarmRepository.Update(alarm);
            await _alarmRepository.SaveChangesAsync();

            await _eventBus.PublishAsync(new AlarmAcknowledgedEvent(alarm.AlarmCode, acknowledgedBy));
            _logService.Info("AlarmService", $"报警已确认：{alarm.AlarmCode}，操作人：{acknowledgedBy}");
        }

        public async Task ClearAlarm(Guid alarmId, string clearedBy)
        {
            var alarm = await _alarmRepository.GetByIdAsync(alarmId);
            if (alarm == null) return;

            alarm.Clear(clearedBy);
            _alarmRepository.Update(alarm);
            await _alarmRepository.SaveChangesAsync();

            _activeAlarms.TryRemove(alarm.AlarmCode, out _);
            _logService.Info("AlarmService", $"报警已清除：{alarm.AlarmCode}，操作人：{clearedBy}");
        }

        public async Task ClearAlarmByCode(string alarmCode, string clearedBy)
        {
            if (_activeAlarms.TryRemove(alarmCode, out var alarm))
            {
                alarm.Clear(clearedBy);
                _alarmRepository.Update(alarm);
                await _alarmRepository.SaveChangesAsync();
            }
        }

        public async Task<IReadOnlyList<AlarmRecord>> GetActiveAlarms()
        {
            return await _alarmRepository.GetActiveAlarmsAsync();
        }

        public async Task<(IReadOnlyList<AlarmRecord> Items, int TotalCount)> GetAlarmHistory(
            DateTime startTime, DateTime endTime,
            AlarmLevel? level = null, AlarmCategory? category = null,
            int pageIndex = 0, int pageSize = 50)
        {
            var results = await _alarmRepository.GetAlarmsByDateRangeAsync(startTime, endTime, category, level);
            var total = results.Count;
            var paged = results.Skip(pageIndex * pageSize).Take(pageSize).ToList();
            return (paged, total);
        }

        /// <summary>从数据库加载活跃报警到内存缓存（系统启动时调用）</summary>
        public async Task LoadActiveAlarmsAsync(CancellationToken cancellationToken = default)
        {
            var activeAlarms = await _alarmRepository.GetActiveAlarmsAsync(cancellationToken);
            foreach (var alarm in activeAlarms)
                _activeAlarms[alarm.AlarmCode] = alarm;
            _logService.Info("AlarmService", $"已加载 {activeAlarms.Count} 条活跃报警到内存缓存");
        }
    }
}
