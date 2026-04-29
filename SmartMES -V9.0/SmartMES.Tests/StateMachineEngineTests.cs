using SmartMES.Core.StateMachine;

namespace SmartMES.Tests;

public class StateMachineEngineTests
{
    [Fact]
    /// <summary>
    /// 自动补齐：BuildStandard_ShouldStartFromIdle 方法说明。
    /// </summary>
    public void BuildStandard_ShouldStartFromIdle()
    {
        var sm = StateMachineEngine.BuildStandard("UT-SM");

        Assert.Equal(MachineState.Idle, sm.CurrentState);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：Fire_ShouldFollowStandardRoute_IdleToRunningToPausedToRunningToIdle 方法说明。
    /// </summary>
    public void Fire_ShouldFollowStandardRoute_IdleToRunningToPausedToRunningToIdle()
    {
        var sm = StateMachineEngine.BuildStandard("UT-SM");

        var startOk = sm.Fire("Start");
        var pauseOk = sm.Fire("Pause");
        var resumeOk = sm.Fire("Resume");
        var stopOk = sm.Fire("Stop");

        Assert.True(startOk);
        Assert.True(pauseOk);
        Assert.True(resumeOk);
        Assert.True(stopOk);
        Assert.Equal(MachineState.Idle, sm.CurrentState);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：Fire_ShouldReturnFalse_WhenTriggerInvalidForCurrentState 方法说明。
    /// </summary>
    public void Fire_ShouldReturnFalse_WhenTriggerInvalidForCurrentState()
    {
        var sm = StateMachineEngine.BuildStandard("UT-SM");

        var ok = sm.Fire("Resume");

        Assert.False(ok);
        Assert.Equal(MachineState.Idle, sm.CurrentState);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：Fire_WithGuard_ShouldBlockThenAllowTransition 方法说明。
    /// </summary>
    public void Fire_WithGuard_ShouldBlockThenAllowTransition()
    {
        var sm = new StateMachineEngine("GuardSM");
        var allow = false;

        sm.AddTransition(new StateTransition
        {
            From = MachineState.Idle,
            To = MachineState.Running,
            Trigger = "Start",
            Guard = () => allow
        });

        var first = sm.Fire("Start");
        allow = true;
        var second = sm.Fire("Start");

        Assert.False(first);
        Assert.True(second);
        Assert.Equal(MachineState.Running, sm.CurrentState);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：ForceState_ShouldAlwaysChangeState 方法说明。
    /// </summary>
    public void ForceState_ShouldAlwaysChangeState()
    {
        var sm = StateMachineEngine.BuildStandard("UT-SM");

        sm.ForceState(MachineState.Alarm);

        Assert.Equal(MachineState.Alarm, sm.CurrentState);
    }
}
