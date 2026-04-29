using SmartMES.Modules.MotionControl;

namespace SmartMES.Tests;

public class AxisControllerTests
{
    [Fact]
    /// <summary>
    /// 自动补齐：MoveTo_ShouldReachTargetAndReturnIdle 方法说明。
    /// </summary>
    public async Task MoveTo_ShouldReachTargetAndReturnIdle()
    {
        var axis = new AxisController("X")
        {
            Velocity = 300,
            Acceleration = 2000
        };

        var started = axis.MoveTo(20);
        Assert.True(started);

        var ok = await WaitUntilAsync(() => axis.State == AxisState.Idle, TimeSpan.FromSeconds(3));

        Assert.True(ok);
        Assert.InRange(axis.Position, 19.8, 20.2);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：PauseAndResume_ShouldSwitchStatesAndFinishMove 方法说明。
    /// </summary>
    public async Task PauseAndResume_ShouldSwitchStatesAndFinishMove()
    {
        var axis = new AxisController("Y")
        {
            Velocity = 150,
            Acceleration = 600
        };

        Assert.True(axis.MoveTo(80));
        await Task.Delay(80);

        var paused = axis.Pause();
        Assert.True(paused);
        Assert.Equal(AxisState.Paused, axis.State);

        await Task.Delay(80);
        var resumed = axis.Resume();
        Assert.True(resumed);

        var ok = await WaitUntilAsync(() => axis.State == AxisState.Idle, TimeSpan.FromSeconds(5));
        Assert.True(ok);
        Assert.InRange(axis.Position, 79.5, 80.5);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：SetErrorThenReset_ShouldRecoverToIdle 方法说明。
    /// </summary>
    public void SetErrorThenReset_ShouldRecoverToIdle()
    {
        var axis = new AxisController("Z");

        var err = axis.SetError("UT error");
        Assert.True(err);
        Assert.Equal(AxisState.Error, axis.State);

        var reset = axis.Reset();
        Assert.True(reset);
        Assert.Equal(AxisState.Idle, axis.State);
    }

    /// <summary>
    /// 自动补齐：WaitUntilAsync 方法说明。
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
                return false;
            await Task.Delay(20);
        }
        return true;
    }
}
