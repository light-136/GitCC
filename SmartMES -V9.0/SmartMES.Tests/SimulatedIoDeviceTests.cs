using SmartMES.Core.IO;

namespace SmartMES.Tests;

public class SimulatedIoDeviceTests
{
    [Fact]
    /// <summary>
    /// 自动补齐：Constructor_ShouldCreateExpectedChannels 方法说明。
    /// </summary>
    public void Constructor_ShouldCreateExpectedChannels()
    {
        var io = new SimulatedIoDevice("UT-IO");

        var channels = io.GetChannels();

        Assert.Equal(40, channels.Count);
        Assert.Contains(channels, c => c.Name == "DI00");
        Assert.Contains(channels, c => c.Name == "DO00");
        Assert.Contains(channels, c => c.Name == "AI0");
        Assert.Contains(channels, c => c.Name == "AO0");
    }

    [Fact]
    /// <summary>
    /// 自动补齐：WriteOutputThenReadInput_ShouldKeepWrittenValueOnOutputAddress 方法说明。
    /// </summary>
    public void WriteOutputThenReadInput_ShouldKeepWrittenValueOnOutputAddress()
    {
        var io = new SimulatedIoDevice("UT-IO");

        io.WriteOutput(100, true);
        var output = io.GetChannels().First(c => c.Address == 100).Value;

        Assert.True(output);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：WriteAnalog_ShouldUpdateAnalogValue 方法说明。
    /// </summary>
    public void WriteAnalog_ShouldUpdateAnalogValue()
    {
        var io = new SimulatedIoDevice("UT-IO");

        io.WriteAnalog(300, 12.34);

        Assert.Equal(12.34, io.ReadAnalog(300), 2);
    }
}
