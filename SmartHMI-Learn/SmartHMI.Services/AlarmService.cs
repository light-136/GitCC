using SmartHMI.Core.Events;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Services;

public class AlarmService : IAlarmService
{
    private readonly List<AlarmRecord> _active = new();
    private readonly List<AlarmRecord> _history = new();
    private readonly IEventBus _eventBus;
    private readonly Lock _lock = new();

    public event EventHandler<AlarmRecord>? AlarmTriggered;
    public event EventHandler<AlarmRecord>? AlarmAcknowledged;

    public IReadOnlyList<AlarmRecord> ActiveAlarms { get { lock (_lock) return _active.ToList(); } }
    public IReadOnlyList<AlarmRecord> AlarmHistory { get { lock (_lock) return _history.TakeLast(500).ToList(); } }

    public AlarmService(IEventBus eventBus) => _eventBus = eventBus;

    public void Trigger(string code, string message, AlarmLevel level, string source = "")
    {
        var alarm = new AlarmRecord { Code = code, Message = message, Level = level, Source = source };
        lock (_lock)
        {
            _active.Add(alarm);
            _history.Add(alarm);
        }
        AlarmTriggered?.Invoke(this, alarm);
        _eventBus.Publish(new NewAlarmEvent { Alarm = alarm });
    }

    public void Acknowledge(Guid alarmId)
    {
        AlarmRecord? alarm;
        lock (_lock)
            alarm = _active.FirstOrDefault(a => a.Id == alarmId);

        if (alarm != null)
        {
            alarm.AcknowledgedAt = DateTime.Now;
            AlarmAcknowledged?.Invoke(this, alarm);
        }
    }

    public void Clear(Guid alarmId)
    {
        lock (_lock)
        {
            var alarm = _active.FirstOrDefault(a => a.Id == alarmId);
            if (alarm != null)
            {
                alarm.ClearedAt = DateTime.Now;
                _active.Remove(alarm);
                _eventBus.Publish(new AlarmClearedEvent { AlarmId = alarmId });
            }
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            foreach (var alarm in _active)
                alarm.ClearedAt = DateTime.Now;
            _active.Clear();
        }
    }
}
