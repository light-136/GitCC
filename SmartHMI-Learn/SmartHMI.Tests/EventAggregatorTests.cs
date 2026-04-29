using SmartHMI.Core.EventBus;
using SmartHMI.Core.Events;
using SmartHMI.Core.Models;

namespace SmartHMI.Tests;

/// <summary>
/// EventAggregator 事件总线单元测试
/// </summary>
public class EventAggregatorTests
{
    [Fact]
    public void Subscribe_And_Publish_ShouldInvokeHandler()
    {
        var bus = new EventAggregator();
        DeviceStatusChangedEvent? received = null;
        bus.Subscribe<DeviceStatusChangedEvent>(e => received = e);

        var evt = new DeviceStatusChangedEvent { DeviceId = "D01", NewStatus = DeviceStatus.Online };
        bus.Publish(evt);

        Assert.NotNull(received);
        Assert.Equal("D01", received.DeviceId);
        Assert.Equal(DeviceStatus.Online, received.NewStatus);
    }

    [Fact]
    public void Unsubscribe_ShouldStopReceivingEvents()
    {
        var bus = new EventAggregator();
        int callCount = 0;
        Action<DeviceStatusChangedEvent> handler = _ => callCount++;

        bus.Subscribe(handler);
        bus.Publish(new DeviceStatusChangedEvent { DeviceId = "D01", NewStatus = DeviceStatus.Online });
        bus.Unsubscribe(handler);
        bus.Publish(new DeviceStatusChangedEvent { DeviceId = "D01", NewStatus = DeviceStatus.Offline });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void MultipleSubscribers_AllShouldReceiveEvent()
    {
        var bus = new EventAggregator();
        int count = 0;
        var alarm = new AlarmRecord { Code = "A001", Message = "测试报警", Level = AlarmLevel.Warning };
        bus.Subscribe<NewAlarmEvent>(_ => count++);
        bus.Subscribe<NewAlarmEvent>(_ => count++);
        bus.Subscribe<NewAlarmEvent>(_ => count++);

        bus.Publish(new NewAlarmEvent { Alarm = alarm });

        Assert.Equal(3, count);
    }

    [Fact]
    public void Publish_WithNoSubscribers_ShouldNotThrow()
    {
        var bus = new EventAggregator();
        var ex = Record.Exception(() => bus.Publish(new UserLoginEvent { IsLogin = true }));
        Assert.Null(ex);
    }

    [Fact]
    public void Subscribe_DifferentEventTypes_ShouldNotCrossfire()
    {
        var bus = new EventAggregator();
        bool alarmReceived = false;
        bool loginReceived = false;
        var alarm = new AlarmRecord { Code = "A001", Message = "测试", Level = AlarmLevel.Info };

        bus.Subscribe<NewAlarmEvent>(_ => alarmReceived = true);
        bus.Subscribe<UserLoginEvent>(_ => loginReceived = true);

        bus.Publish(new NewAlarmEvent { Alarm = alarm });

        Assert.True(alarmReceived);
        Assert.False(loginReceived);
    }
}
