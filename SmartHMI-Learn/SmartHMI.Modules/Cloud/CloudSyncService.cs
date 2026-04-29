using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartHMI.Modules.Cloud;

public class CloudSyncService : ICloudSyncService
{
    private readonly Queue<CloudSyncRecord> _queue = new();
    private readonly List<CloudSyncRecord> _history = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Lock _lock = new();
    private readonly System.Timers.Timer _autoFlushTimer;

    public bool IsEnabled { get; private set; } = true;
    public string CloudEndpoint { get; set; } = "https://api.smarthmi.example.com/v1/data";
    public int PendingCount { get { lock (_lock) return _queue.Count; } }

    public event EventHandler<CloudSyncRecord>? SyncCompleted;

    public CloudSyncService()
    {
        // 每 30 秒自动尝试同步
        _autoFlushTimer = new System.Timers.Timer(30_000);
        _autoFlushTimer.Elapsed += async (_, _) => await FlushAsync();
        _autoFlushTimer.Start();
    }

    public async Task EnqueueAsync(CloudSyncDataType dataType, object payload)
    {
        var record = new CloudSyncRecord
        {
            DataType = dataType,
            Payload = JsonSerializer.Serialize(payload)
        };
        lock (_lock) _queue.Enqueue(record);
        await Task.CompletedTask;
    }

    public async Task<int> FlushAsync()
    {
        if (!IsEnabled) return 0;

        List<CloudSyncRecord> batch;
        lock (_lock)
        {
            batch = new List<CloudSyncRecord>();
            while (_queue.Count > 0 && batch.Count < 50)
                batch.Add(_queue.Dequeue());
        }

        int successCount = 0;
        foreach (var record in batch)
        {
            record.Status = CloudSyncStatus.Syncing;
            try
            {
                // 仿真：模拟 HTTP 上传（实际项目替换为真实 API 调用）
                await Task.Delay(50);
                // var response = await _http.PostAsJsonAsync(CloudEndpoint, record);
                // response.EnsureSuccessStatusCode();

                record.Status = CloudSyncStatus.Success;
                record.SyncedAt = DateTime.Now;
                successCount++;
            }
            catch (Exception ex)
            {
                record.Status = CloudSyncStatus.Failed;
                record.ErrorMessage = ex.Message;
                record.RetryCount++;
                if (record.RetryCount < 3)
                    lock (_lock) _queue.Enqueue(record);
            }

            lock (_lock)
            {
                _history.Add(record);
                if (_history.Count > 200) _history.RemoveAt(0);
            }
            SyncCompleted?.Invoke(this, record);
        }

        return successCount;
    }

    public IReadOnlyList<CloudSyncRecord> GetRecentRecords(int count = 50)
    { lock (_lock) return _history.TakeLast(count).ToList(); }
}
