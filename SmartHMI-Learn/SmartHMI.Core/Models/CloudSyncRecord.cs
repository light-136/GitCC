namespace SmartHMI.Core.Models;

public enum CloudSyncStatus { Pending, Syncing, Success, Failed }
public enum CloudSyncDataType { Alarm, Log, DeviceStatus, ProductionData, Recipe }

public class CloudSyncRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CloudSyncDataType DataType { get; set; }
    public string Payload { get; set; } = "";
    public CloudSyncStatus Status { get; set; } = CloudSyncStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? SyncedAt { get; set; }
    public int RetryCount { get; set; }
}
