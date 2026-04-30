using SmartMES.Modules.MotionControl;

namespace SmartMES.Tests;

public class TenAxisControllerTests
{
    [Fact]
    /// <summary>
    /// 测试构造函数应创建10个轴。
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
    /// 测试使用简化程序执行 G 代码应成功。
    /// </summary>
    public async Task RunGCodeAsync_WithSampleProgram_ShouldSucceed()
    {
        var ctrl = new TenAxisController();
        var program = "G0 X10 Y10 Z5\nG1 X20 Y20 F3000\nM2";

        var result = await ctrl.RunGCodeAsync(program);

        Assert.True(result.Success);
        Assert.True(result.ExecutedLines > 0);
        Assert.Contains("完成", result.Message);
    }

    [Fact]
    /// <summary>
    /// 测试 G 代码执行取消后应返回取消结果。
    /// </summary>
    public async Task RunGCodeAsync_WhenCancelled_ShouldReturnCancelledResult()
    {
        var ctrl = new TenAxisController();
        using var cts = new CancellationTokenSource();

        var longProgram = string.Join("\n", Enumerable.Repeat("G1 X50 Y50 Z10 F600", 50));
        var task = ctrl.RunGCodeAsync(longProgram, cts.Token);

        await Task.Delay(50);
        cts.Cancel();

        var result = await task;

        Assert.False(result.Success);
        Assert.Contains("取消", result.Message);
    }
}
