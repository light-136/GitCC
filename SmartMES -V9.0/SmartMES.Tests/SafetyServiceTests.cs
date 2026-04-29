using SmartMES.Core.Safety;

namespace SmartMES.Tests;

public class SafetyServiceTests
{
    [Fact]
    /// <summary>
    /// 自动补齐：StartDevice_ShouldBeBlocked_WhenDoorOpen 方法说明。
    /// </summary>
    public void StartDevice_ShouldBeBlocked_WhenDoorOpen()
    {
        var safety = new SafetyService { DoorClosed = false };

        var ok = safety.IsSafeToOperate("StartDevice");

        Assert.False(ok);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：TriggerAndResetEStop_ShouldChangeState 方法说明。
    /// </summary>
    public void TriggerAndResetEStop_ShouldChangeState()
    {
        var safety = new SafetyService();

        safety.TriggerEStop("UT");
        Assert.True(safety.IsEStopActive);

        safety.ResetEStop();

        Assert.False(safety.IsEStopActive);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：AddInterlock_ShouldBlockMatchingOperation 方法说明。
    /// </summary>
    public void AddInterlock_ShouldBlockMatchingOperation()
    {
        var safety = new SafetyService();
        safety.AddInterlock("OpX", new InterlockCondition
        {
            Name = "UT-Lock",
            Type = InterlockType.PreCondition,
            Description = "always block",
            Check = () => false
        });

        var ok = safety.IsSafeToOperate("OpX");

        Assert.False(ok);
    }
}
