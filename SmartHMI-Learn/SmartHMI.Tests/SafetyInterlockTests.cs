using SmartHMI.Modules.Safety;

namespace SmartHMI.Tests;

public class SafetyInterlockTests
{
    [Fact]
    public void IsAllSafe_WithNoConditions_ShouldBeTrue()
    {
        var svc = new SafetyInterlockService();
        Assert.True(svc.IsAllSafe);
    }

    [Fact]
    public void RegisterCondition_AllTrue_IsAllSafeShouldBeTrue()
    {
        var svc = new SafetyInterlockService();
        svc.RegisterCondition("门禁", () => true, "门已关闭");
        svc.RegisterCondition("气压", () => true, "气压正常");
        Assert.True(svc.IsAllSafe);
    }

    [Fact]
    public void RegisterCondition_OneFalse_IsAllSafeShouldBeFalse()
    {
        var svc = new SafetyInterlockService();
        svc.RegisterCondition("门禁", () => true);
        svc.RegisterCondition("气压", () => false);
        Assert.False(svc.CheckAll());
    }

    [Fact]
    public void TriggerEStop_ShouldSetEStopActive()
    {
        var svc = new SafetyInterlockService();
        svc.TriggerEStop("测试急停");
        Assert.True(svc.IsEStopActive);
    }

    [Fact]
    public void TriggerEStop_ShouldFireEvent()
    {
        var svc = new SafetyInterlockService();
        string? reason = null;
        svc.EStopTriggered += (_, r) => reason = r;
        svc.TriggerEStop("紧急停止");
        Assert.Equal("紧急停止", reason);
    }

    [Fact]
    public void ResetEStop_WhenAllSafe_ShouldClearEStop()
    {
        var svc = new SafetyInterlockService();
        svc.TriggerEStop("测试");
        svc.ResetEStop();
        Assert.False(svc.IsEStopActive);
    }

    [Fact]
    public void ResetEStop_WhenConditionFails_ShouldNotReset()
    {
        var svc = new SafetyInterlockService();
        svc.RegisterCondition("气压", () => false);
        svc.TriggerEStop("测试");
        svc.ResetEStop();
        Assert.True(svc.IsEStopActive);
    }

    [Fact]
    public void UnregisterCondition_ShouldRemoveIt()
    {
        var svc = new SafetyInterlockService();
        svc.RegisterCondition("气压", () => false);
        svc.UnregisterCondition("气压");
        Assert.True(svc.CheckAll());
    }

    [Fact]
    public void GetConditions_ShouldReturnAllRegistered()
    {
        var svc = new SafetyInterlockService();
        svc.RegisterCondition("A", () => true, "描述A");
        svc.RegisterCondition("B", () => false, "描述B");
        var conditions = svc.GetConditions();
        Assert.Equal(2, conditions.Count);
        Assert.Contains(conditions, c => c.Name == "A" && c.IsSafe);
        Assert.Contains(conditions, c => c.Name == "B" && !c.IsSafe);
    }
}
