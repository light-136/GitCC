namespace SmartHMI.Core.Models;

public enum AxisState { Idle, Homing, Moving, Jogging, Faulted, Disabled }

public class AxisModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public AxisState State { get; set; } = AxisState.Disabled;
    public double Position { get; set; }
    public double TargetPosition { get; set; }
    public double Velocity { get; set; }
    public double MaxVelocity { get; set; } = 100.0;
    public double Acceleration { get; set; } = 50.0;
    public bool IsEnabled { get; set; }
    public bool IsHomed { get; set; }
    public bool PositiveLimitActive { get; set; }
    public bool NegativeLimitActive { get; set; }
    public string? FaultMessage { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}
