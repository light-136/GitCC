using SmartMES.Modules.MotionControl;

namespace SmartMES.Tests;

public class GCodeCommandTests
{
    [Fact]
    /// <summary>
    /// 自动补齐：Parse_G1Line_ShouldParseTypeAxisAndFeedRate 方法说明。
    /// </summary>
    public void Parse_G1Line_ShouldParseTypeAxisAndFeedRate()
    {
        var cmd = GCodeCommand.Parse("G1 X100.5 Y-20.2 Z0 F1800");

        Assert.Equal(GCodeType.G1, cmd.Type);
        Assert.Equal(100.5, cmd.AxisPositions["X"], 3);
        Assert.Equal(-20.2, cmd.AxisPositions["Y"], 3);
        Assert.Equal(0, cmd.AxisPositions["Z"], 3);
        Assert.Equal(1800, cmd.FeedRate, 3);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：Parse_M2Line_ShouldParseProgramEnd 方法说明。
    /// </summary>
    public void Parse_M2Line_ShouldParseProgramEnd()
    {
        var cmd = GCodeCommand.Parse("M2");

        Assert.Equal(GCodeType.M2, cmd.Type);
        Assert.Empty(cmd.AxisPositions);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：Parse_UnknownToken_ShouldReturnUnknownType 方法说明。
    /// </summary>
    public void Parse_UnknownToken_ShouldReturnUnknownType()
    {
        var cmd = GCodeCommand.Parse("G99 X10");

        Assert.Equal(GCodeType.Unknown, cmd.Type);
        Assert.Equal(10, cmd.AxisPositions["X"], 3);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：Parse_BlankLine_ShouldKeepUnknownAndNoAxis 方法说明。
    /// </summary>
    public void Parse_BlankLine_ShouldKeepUnknownAndNoAxis()
    {
        var cmd = GCodeCommand.Parse("   ");

        Assert.Equal(GCodeType.Unknown, cmd.Type);
        Assert.Empty(cmd.AxisPositions);
    }
}
