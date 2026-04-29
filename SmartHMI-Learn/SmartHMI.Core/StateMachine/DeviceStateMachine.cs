namespace SmartHMI.Core.StateMachine;

public enum DeviceState
{
    Idle,
    Initializing,
    Ready,
    Running,
    Paused,
    Stopping,
    Faulted,
    Maintenance,
    EStop
}

public class StateTransition
{
    public DeviceState From { get; init; }
    public DeviceState To { get; init; }
    public string Trigger { get; init; } = "";
    public Func<bool>? Guard { get; init; }
    public Action? OnTransition { get; init; }
}

public class DeviceStateMachine
{
    private DeviceState _current = DeviceState.Idle;
    private readonly List<StateTransition> _transitions = new();
    private readonly Lock _lock = new();

    public DeviceState Current
    {
        get { lock (_lock) return _current; }
    }

    public event EventHandler<(DeviceState From, DeviceState To, string Trigger)>? StateChanged;

    public DeviceStateMachine()
    {
        // Standard industrial device state transitions
        AddTransition(DeviceState.Idle, DeviceState.Initializing, "Initialize");
        AddTransition(DeviceState.Initializing, DeviceState.Ready, "InitComplete");
        AddTransition(DeviceState.Initializing, DeviceState.Faulted, "InitFailed");
        AddTransition(DeviceState.Ready, DeviceState.Running, "Start");
        AddTransition(DeviceState.Running, DeviceState.Paused, "Pause");
        AddTransition(DeviceState.Paused, DeviceState.Running, "Resume");
        AddTransition(DeviceState.Running, DeviceState.Stopping, "Stop");
        AddTransition(DeviceState.Stopping, DeviceState.Idle, "StopComplete");
        AddTransition(DeviceState.Faulted, DeviceState.Idle, "Reset");
        AddTransition(DeviceState.Ready, DeviceState.Maintenance, "EnterMaintenance");
        AddTransition(DeviceState.Maintenance, DeviceState.Idle, "ExitMaintenance");

        // EStop can be triggered from any state
        foreach (var state in Enum.GetValues<DeviceState>())
        {
            if (state != DeviceState.EStop)
                AddTransition(state, DeviceState.EStop, "EStop");
        }
        AddTransition(DeviceState.EStop, DeviceState.Idle, "EStopReset");
    }

    public void AddTransition(DeviceState from, DeviceState to, string trigger,
        Func<bool>? guard = null, Action? onTransition = null)
    {
        _transitions.Add(new StateTransition
        {
            From = from, To = to, Trigger = trigger,
            Guard = guard, OnTransition = onTransition
        });
    }

    public bool Fire(string trigger)
    {
        lock (_lock)
        {
            var transition = _transitions.FirstOrDefault(t =>
                t.From == _current && t.Trigger == trigger &&
                (t.Guard == null || t.Guard()));

            if (transition == null) return false;

            var from = _current;
            _current = transition.To;
            transition.OnTransition?.Invoke();
            StateChanged?.Invoke(this, (from, _current, trigger));
            return true;
        }
    }

    public bool CanFire(string trigger)
    {
        lock (_lock)
        {
            return _transitions.Any(t =>
                t.From == _current && t.Trigger == trigger &&
                (t.Guard == null || t.Guard()));
        }
    }
}
