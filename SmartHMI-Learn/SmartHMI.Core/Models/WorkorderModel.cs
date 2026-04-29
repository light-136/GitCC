namespace SmartHMI.Core.Models;

public enum WorkorderStatus { Pending, Running, Paused, Completed, Cancelled }

public class WorkorderModel
{
    public string Id { get; set; } = "";
    public string ProductType { get; set; } = "";
    public string RecipeId { get; set; } = "";
    public int PlannedQty { get; set; }
    public int CompletedQty { get; set; }
    public int NgQty { get; set; }
    public WorkorderStatus Status { get; set; } = WorkorderStatus.Pending;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string OperatorId { get; set; } = "";
    public double Yield => PlannedQty > 0 ? (double)(CompletedQty - NgQty) / PlannedQty * 100 : 0;
}
