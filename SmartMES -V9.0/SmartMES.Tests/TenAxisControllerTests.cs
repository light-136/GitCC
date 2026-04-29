using SmartMES.Modules.MotionControl;

namespace SmartMES.Tests;

public class TenAxisControllerTests
{
    [Fact]
    /// <summary>
    /// 自动补齐：Constructor_ShouldCreateTenAxes 方法说明。
    /// </summary>
    public void Constructor_ShouldCreateTenAxes()
    {
        var ctrl = new TenAxisController();

        Assert.Equal(10, ctrl.Axes.Count);
        Assert.Contains("X", ctrl.Axes.Keys);
        Assert.Contains("S", ctrl.Axes.Keys);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：RunGCodeAsync_WithSampleProgram_ShouldSucceed 方法说明。
    /// </summary>
    public async Task RunGCodeAsync_WithSampleProgram_ShouldSucceed()
    {
        var ctrl = new TenAxisController();
        var sample = TenAxisController.GetSampleProgram();

        var result = await ctrl.RunGCodeAsync(sample);

        Assert.True(result.Success);
        Assert.True(result.ExecutedLines > 0);
        Assert.Contains("绋嬪簭瀹屾垚", result.Message);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：RunGCodeAsync_WhenCancelled_ShouldReturnCancelledResult 方法说明。
    /// </summary>
    public async Task RunGCodeAsync_WhenCancelled_ShouldReturnCancelledResult()
    {
        var ctrl = new TenAxisController();
        using var cts = new CancellationTokenSource();

        var longProgram = string.Join("\n", Enumerable.Repeat("G1 X100 Y100 Z50 F600", 200));
        var task = ctrl.RunGCodeAsync(longProgram, cts.Token);

        await Task.Delay(80);
        cts.Cancel();

        var result = await task;

        Assert.False(result.Success);
        Assert.Contains("鍙栨秷", result.Message);
    }
}
