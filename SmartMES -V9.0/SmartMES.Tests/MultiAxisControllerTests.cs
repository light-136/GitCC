using SmartMES.Modules.MotionControl;

namespace SmartMES.Tests;

public class MultiAxisControllerTests
{
    [Fact]
    /// <summary>
    /// 自动补齐：HomeAllAsync_ShouldBringAllAxesToIdleAndZero 方法说明。
    /// </summary>
    public async Task HomeAllAsync_ShouldBringAllAxesToIdleAndZero()
    {
        var ctrl = new MultiAxisController();
        ctrl.AddAxis("X", 200, 800);
        ctrl.AddAxis("Y", 200, 800);

        ctrl.Axes["X"].MoveTo(30);
        ctrl.Axes["Y"].MoveTo(40);
        await WaitUntilAsync(() => ctrl.Axes.Values.All(a => a.State == AxisState.Idle), TimeSpan.FromSeconds(5));

        await ctrl.HomeAllAsync();

        Assert.All(ctrl.Axes.Values, a =>
        {
            Assert.Equal(AxisState.Idle, a.State);
            Assert.InRange(a.Position, -0.1, 0.1);
        });
    }

    [Fact]
    /// <summary>
    /// 自动补齐：LinearInterpolateAsync_ShouldMoveAxesToTargets 方法说明。
    /// </summary>
    public async Task LinearInterpolateAsync_ShouldMoveAxesToTargets()
    {
        var ctrl = new MultiAxisController();
        ctrl.AddAxis("X", 300, 1200);
        ctrl.AddAxis("Y", 300, 1200);

        await ctrl.LinearInterpolateAsync(new InterpolationPoint
        {
            FeedRate = 180,
            AxisTargets = new Dictionary<string, double>
            {
                ["X"] = 50,
                ["Y"] = 80
            }
        });

        Assert.InRange(ctrl.Axes["X"].Position, 49.5, 50.5);
        Assert.InRange(ctrl.Axes["Y"].Position, 79.5, 80.5);
    }

    /// <summary>
    /// 自动补齐：WaitUntilAsync 方法说明。
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout) return false;
            await Task.Delay(20);
        }
        return true;
    }
}
