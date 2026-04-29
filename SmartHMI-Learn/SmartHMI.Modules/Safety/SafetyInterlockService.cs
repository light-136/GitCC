using SmartHMI.Core.Interfaces;

namespace SmartHMI.Modules.Safety;

public class SafetyInterlockService : ISafetyInterlockService
{
    private readonly record struct Condition(string Name, Func<bool> Check, string Description);

    private readonly List<Condition> _conditions = new();
    private readonly Lock _lock = new();
    private bool _eStopActive;

    public bool IsEStopActive => _eStopActive;
    public bool IsAllSafe => !_eStopActive && CheckAll();

    public event EventHandler<string>? EStopTriggered;
    public event EventHandler? EStopReset;
    public event EventHandler<(string Name, bool IsSafe)>? ConditionChanged;

    public void RegisterCondition(string name, Func<bool> condition, string description = "")
    {
        lock (_lock)
        {
            _conditions.RemoveAll(c => c.Name == name);
            _conditions.Add(new Condition(name, condition, description));
        }
    }

    public void UnregisterCondition(string name)
    {
        lock (_lock) { _conditions.RemoveAll(c => c.Name == name); }
    }

    public bool CheckAll()
    {
        List<Condition> snapshot;
        lock (_lock) snapshot = new(_conditions);
        return snapshot.All(c => c.Check());
    }

    public void TriggerEStop(string reason)
    {
        _eStopActive = true;
        EStopTriggered?.Invoke(this, reason);
    }

    public void ResetEStop()
    {
        if (!CheckAll()) return;
        _eStopActive = false;
        EStopReset?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<(string Name, bool IsSafe, string Description)> GetConditions()
    {
        lock (_lock)
            return _conditions.Select(c => (c.Name, c.Check(), c.Description)).ToList();
    }
}
