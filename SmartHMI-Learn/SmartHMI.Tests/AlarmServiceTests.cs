using SmartHMI.Core.EventBus;
using SmartHMI.Core.Models;
using SmartHMI.Services;

namespace SmartHMI.Tests;

/// <summary>
/// AlarmService 报警服务单元测试
/// </summary>
public class AlarmServiceTests
{
    private static AlarmService CreateService() => new(new EventAggregator());

    [Fact]
    public void Trigger_ShouldAddToActiveAlarms()
    {
        var svc = CreateService();
        svc.Trigger("A001", "温度过高", AlarmLevel.Warning, "Sensor01");

        Assert.Single(svc.ActiveAlarms);
        Assert.Equal("A001", svc.ActiveAlarms[0].Code);
        Assert.Equal("温度过高", svc.ActiveAlarms[0].Message);
        Assert.True(svc.ActiveAlarms[0].IsActive);
    }

    [Fact]
    public void Trigger_ShouldPublishNewAlarmEvent()
    {
        var bus = new EventAggregator();
        var svc = new AlarmService(bus);
        Core.Events.NewAlarmEvent? received = null;
        bus.Subscribe<Core.Events.NewAlarmEvent>(e => received = e);

        svc.Trigger("A002", "压力异常", AlarmLevel.Error, "Sensor02");

        Assert.NotNull(received);
        Assert.Equal("A002", received.Alarm.Code);
    }

    [Fact]
    public void Acknowledge_ShouldSetAcknowledgedAt()
    {
        var svc = CreateService();
        svc.Trigger("A001", "测试", AlarmLevel.Info);
        var alarm = svc.ActiveAlarms[0];

        svc.Acknowledge(alarm.Id);

        Assert.True(alarm.IsAcknowledged);
        Assert.NotNull(alarm.AcknowledgedAt);
    }

    [Fact]
    public void Clear_ShouldRemoveFromActiveAlarms()
    {
        var svc = CreateService();
        svc.Trigger("A001", "测试", AlarmLevel.Warning);
        var alarmId = svc.ActiveAlarms[0].Id;

        svc.Clear(alarmId);

        Assert.Empty(svc.ActiveAlarms);
    }

    [Fact]
    public void Clear_ShouldPublishAlarmClearedEvent()
    {
        var bus = new EventAggregator();
        var svc = new AlarmService(bus);
        Core.Events.AlarmClearedEvent? received = null;
        bus.Subscribe<Core.Events.AlarmClearedEvent>(e => received = e);

        svc.Trigger("A001", "测试", AlarmLevel.Warning);
        var alarmId = svc.ActiveAlarms[0].Id;
        svc.Clear(alarmId);

        Assert.NotNull(received);
        Assert.Equal(alarmId, received.AlarmId);
    }

    [Fact]
    public void ClearAll_ShouldEmptyActiveAlarms()
    {
        var svc = CreateService();
        svc.Trigger("A001", "报警1", AlarmLevel.Warning);
        svc.Trigger("A002", "报警2", AlarmLevel.Error);
        svc.Trigger("A003", "报警3", AlarmLevel.Critical);

        svc.ClearAll();

        Assert.Empty(svc.ActiveAlarms);
    }

    [Fact]
    public void AlarmHistory_ShouldRetainClearedAlarms()
    {
        var svc = CreateService();
        svc.Trigger("A001", "测试", AlarmLevel.Info);
        var alarmId = svc.ActiveAlarms[0].Id;
        svc.Clear(alarmId);

        Assert.Single(svc.AlarmHistory);
        Assert.Equal("A001", svc.AlarmHistory[0].Code);
    }
}
