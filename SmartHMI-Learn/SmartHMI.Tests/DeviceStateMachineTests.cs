using SmartHMI.Core.StateMachine;

namespace SmartHMI.Tests;

/// <summary>
/// DeviceStateMachine 状态机单元测试
/// </summary>
public class DeviceStateMachineTests
{
    [Fact]
    public void InitialState_ShouldBeIdle()
    {
        var sm = new DeviceStateMachine();
        Assert.Equal(DeviceState.Idle, sm.Current);
    }

    [Fact]
    public void Fire_Initialize_ShouldTransitionToInitializing()
    {
        var sm = new DeviceStateMachine();
        var result = sm.Fire("Initialize");

        Assert.True(result);
        Assert.Equal(DeviceState.Initializing, sm.Current);
    }

    [Fact]
    public void Fire_InvalidTrigger_ShouldReturnFalse()
    {
        var sm = new DeviceStateMachine();
        var result = sm.Fire("Start"); // Start is only valid from Ready

        Assert.False(result);
        Assert.Equal(DeviceState.Idle, sm.Current); // State unchanged
    }

    [Fact]
    public void FullHappyPath_IdleToRunning()
    {
        var sm = new DeviceStateMachine();

        Assert.True(sm.Fire("Initialize"));
        Assert.Equal(DeviceState.Initializing, sm.Current);

        Assert.True(sm.Fire("InitComplete"));
        Assert.Equal(DeviceState.Ready, sm.Current);

        Assert.True(sm.Fire("Start"));
        Assert.Equal(DeviceState.Running, sm.Current);
    }

    [Fact]
    public void PauseAndResume_ShouldWork()
    {
        var sm = new DeviceStateMachine();
        sm.Fire("Initialize");
        sm.Fire("InitComplete");
        sm.Fire("Start");

        Assert.True(sm.Fire("Pause"));
        Assert.Equal(DeviceState.Paused, sm.Current);

        Assert.True(sm.Fire("Resume"));
        Assert.Equal(DeviceState.Running, sm.Current);
    }

    [Fact]
    public void EStop_FromRunning_ShouldTransitionToEStop()
    {
        var sm = new DeviceStateMachine();
        sm.Fire("Initialize");
        sm.Fire("InitComplete");
        sm.Fire("Start");

        Assert.True(sm.Fire("EStop"));
        Assert.Equal(DeviceState.EStop, sm.Current);
    }

    [Fact]
    public void EStop_FromIdle_ShouldAlsoWork()
    {
        var sm = new DeviceStateMachine();
        Assert.True(sm.Fire("EStop"));
        Assert.Equal(DeviceState.EStop, sm.Current);
    }

    [Fact]
    public void EStopReset_ShouldReturnToIdle()
    {
        var sm = new DeviceStateMachine();
        sm.Fire("EStop");

        Assert.True(sm.Fire("EStopReset"));
        Assert.Equal(DeviceState.Idle, sm.Current);
    }

    [Fact]
    public void StateChanged_EventShouldFire()
    {
        var sm = new DeviceStateMachine();
        (DeviceState From, DeviceState To, string Trigger)? change = null;
        sm.StateChanged += (_, args) => change = args;

        sm.Fire("Initialize");

        Assert.NotNull(change);
        Assert.Equal(DeviceState.Idle, change.Value.From);
        Assert.Equal(DeviceState.Initializing, change.Value.To);
        Assert.Equal("Initialize", change.Value.Trigger);
    }

    [Fact]
    public void CanFire_ShouldReturnCorrectly()
    {
        var sm = new DeviceStateMachine();

        Assert.True(sm.CanFire("Initialize"));
        Assert.False(sm.CanFire("Start"));
        Assert.True(sm.CanFire("EStop"));
    }

    [Fact]
    public void FaultPath_InitFailedAndReset()
    {
        var sm = new DeviceStateMachine();
        sm.Fire("Initialize");

        Assert.True(sm.Fire("InitFailed"));
        Assert.Equal(DeviceState.Faulted, sm.Current);

        Assert.True(sm.Fire("Reset"));
        Assert.Equal(DeviceState.Idle, sm.Current);
    }
}
