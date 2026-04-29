namespace SmartHMI.Modules.Program;

public enum ProgramState { Idle, Running, Paused, Completed, Faulted }

public class ProgramExecutor
{
    private readonly List<ProgramStep> _steps = new();
    private ProgramState _state = ProgramState.Idle;
    private int _currentIndex;
    private CancellationTokenSource? _cts;
    private readonly Lock _lock = new();

    public ProgramState State => _state;
    public int CurrentStepIndex => _currentIndex;
    public IReadOnlyList<ProgramStep> Steps => _steps;
    public string ProgramName { get; set; } = "DefaultProgram";

    public event EventHandler<ProgramStep>? StepStarted;
    public event EventHandler<ProgramStep>? StepCompleted;
    public event EventHandler<ProgramStep>? StepFailed;
    public event EventHandler<ProgramState>? StateChanged;

    public void AddStep(ProgramStep step) => _steps.Add(step);

    public void ClearSteps()
    {
        if (_state == ProgramState.Running) return;
        _steps.Clear();
        _currentIndex = 0;
    }

    public async Task<bool> RunAsync(CancellationToken external = default)
    {
        lock (_lock)
        {
            if (_state == ProgramState.Running) return false;
            _state = ProgramState.Running;
            _currentIndex = 0;
        }
        SetState(ProgramState.Running);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);

        foreach (var step in _steps)
        {
            if (_cts.Token.IsCancellationRequested) break;

            while (_state == ProgramState.Paused)
                await Task.Delay(100, _cts.Token);

            step.Status = StepStatus.Running;
            step.StartedAt = DateTime.Now;
            StepStarted?.Invoke(this, step);

            bool ok = await ExecuteStepAsync(step, _cts.Token);

            step.CompletedAt = DateTime.Now;
            step.Status = ok ? StepStatus.Completed : StepStatus.Failed;

            if (ok)
                StepCompleted?.Invoke(this, step);
            else
            {
                StepFailed?.Invoke(this, step);
                SetState(ProgramState.Faulted);
                return false;
            }

            _currentIndex++;
        }

        SetState(ProgramState.Completed);
        return true;
    }

    public void Pause() { if (_state == ProgramState.Running) SetState(ProgramState.Paused); }
    public void Resume() { if (_state == ProgramState.Paused) SetState(ProgramState.Running); }
    public void Abort() { _cts?.Cancel(); SetState(ProgramState.Idle); }
    public void Reset() { if (_state != ProgramState.Running) { _currentIndex = 0; foreach (var s in _steps) s.Status = StepStatus.Pending; SetState(ProgramState.Idle); } }

    private async Task<bool> ExecuteStepAsync(ProgramStep step, CancellationToken ct)
    {
        try
        {
            return step.Type switch
            {
                StepType.Delay => await ExecuteDelay(step, ct),
                StepType.Condition => step.Condition?.Invoke() ?? true,
                StepType.Action => step.Execute != null ? await step.Execute() : true,
                _ => true
            };
        }
        catch (Exception ex)
        {
            step.ErrorMessage = ex.Message;
            return false;
        }
    }

    private static async Task<bool> ExecuteDelay(ProgramStep step, CancellationToken ct)
    {
        await Task.Delay(step.Delay, ct);
        return true;
    }

    private void SetState(ProgramState s)
    {
        _state = s;
        StateChanged?.Invoke(this, s);
    }
}
