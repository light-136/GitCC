namespace SmartHMI.Core.Interfaces;

public interface ISafetyInterlockService
{
    bool IsAllSafe { get; }
    bool IsEStopActive { get; }
    void RegisterCondition(string name, Func<bool> condition, string description = "");
    void UnregisterCondition(string name);
    bool CheckAll();
    void TriggerEStop(string reason);
    void ResetEStop();
    IReadOnlyList<(string Name, bool IsSafe, string Description)> GetConditions();
    event EventHandler<string>? EStopTriggered;
    event EventHandler? EStopReset;
    event EventHandler<(string Name, bool IsSafe)>? ConditionChanged;
}
