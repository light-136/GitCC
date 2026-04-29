namespace SmartHMI.Modules.Program;

public enum StepType { Action, Condition, Delay, Loop, SubProgram }
public enum StepStatus { Pending, Running, Completed, Failed, Skipped }

public class ProgramStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public StepType Type { get; set; }
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public Func<Task<bool>>? Execute { get; set; }
    public Func<bool>? Condition { get; set; }
    public TimeSpan Delay { get; set; }
    public int MaxRetries { get; set; } = 1;
    public string? NextStepOnSuccess { get; set; }
    public string? NextStepOnFail { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
