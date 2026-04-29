using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using System.Collections.ObjectModel;

namespace SmartHMI.UI.ViewModels;

public class CloudSyncViewModel : BaseViewModel
{
    private readonly ICloudSyncService _cloud;

    public bool IsEnabled => _cloud.IsEnabled;
    public int PendingCount => _cloud.PendingCount;

    private string _endpoint;
    public string Endpoint { get => _endpoint; set { SetField(ref _endpoint, value); _cloud.CloudEndpoint = value; } }

    private string _statusMessage = "就绪";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    private bool _isSyncing;
    public bool IsSyncing { get => _isSyncing; set => SetField(ref _isSyncing, value); }

    public ObservableCollection<CloudSyncRecord> Records { get; } = new();

    public RelayCommand FlushCommand { get; }
    public RelayCommand EnqueueTestCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public CloudSyncViewModel(ICloudSyncService cloud)
    {
        _cloud = cloud;
        _endpoint = _cloud.CloudEndpoint;

        _cloud.SyncCompleted += (_, r) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Records.Insert(0, r);
                if (Records.Count > 50) Records.RemoveAt(Records.Count - 1);
                OnPropertyChanged(nameof(PendingCount));
            });
        };

        FlushCommand = new RelayCommand(async _ => await Flush(), _ => !IsSyncing);
        EnqueueTestCommand = new RelayCommand(async _ => await EnqueueTest());
        RefreshCommand = new RelayCommand(_ => Refresh());
        Refresh();
    }

    private async Task Flush()
    {
        IsSyncing = true;
        StatusMessage = "正在同步...";
        var count = await _cloud.FlushAsync();
        StatusMessage = $"同步完成，成功 {count} 条";
        IsSyncing = false;
        OnPropertyChanged(nameof(PendingCount));
    }

    private async Task EnqueueTest()
    {
        await _cloud.EnqueueAsync(CloudSyncDataType.DeviceStatus, new { DeviceId = "D01", Status = "Online", Timestamp = DateTime.Now });
        StatusMessage = "已加入队列";
        OnPropertyChanged(nameof(PendingCount));
    }

    private void Refresh()
    {
        Records.Clear();
        foreach (var r in _cloud.GetRecentRecords(50)) Records.Add(r);
    }
}
