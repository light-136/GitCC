using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface ICloudSyncService
{
    bool IsEnabled { get; }
    string CloudEndpoint { get; set; }
    int PendingCount { get; }
    Task EnqueueAsync(CloudSyncDataType dataType, object payload);
    Task<int> FlushAsync();
    IReadOnlyList<CloudSyncRecord> GetRecentRecords(int count = 50);
    event EventHandler<CloudSyncRecord>? SyncCompleted;
}
