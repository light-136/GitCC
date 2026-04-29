namespace SmartHMI.Core.Models;

public enum VisionResultType { OK, NG, Uncertain }

public class VisionResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CameraId { get; set; } = "";
    public string JobName { get; set; } = "";
    public VisionResultType ResultType { get; set; }
    public double Confidence { get; set; }
    public string DefectType { get; set; } = "";
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double OffsetAngle { get; set; }
    public string ImagePath { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public Dictionary<string, double> Measurements { get; set; } = new();
}
