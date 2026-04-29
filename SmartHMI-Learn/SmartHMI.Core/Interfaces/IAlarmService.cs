using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface IAlarmService
{
    IReadOnlyList<AlarmRecord> ActiveAlarms { get; }
    IReadOnlyList<AlarmRecord> AlarmHistory { get; }
    void Trigger(string code, string message, AlarmLevel level, string source = "");
    void Acknowledge(Guid alarmId);
    void Clear(Guid alarmId);
    void ClearAll();
    event EventHandler<AlarmRecord>? AlarmTriggered;
    event EventHandler<AlarmRecord>? AlarmAcknowledged;
}
