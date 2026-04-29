using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using SmartHMI.Modules.Ai;
using System.Collections.ObjectModel;

namespace SmartHMI.UI.ViewModels;

public class VisionViewModel : BaseViewModel
{
    private readonly IVisionService _vision;
    private readonly VisionAnalysisService _analysis;

    public ObservableCollection<VisionResult> Results { get; } = new();

    private VisionResult? _selectedResult;
    public VisionResult? SelectedResult { get => _selectedResult; set => SetField(ref _selectedResult, value); }

    private string _jobName = "InspectA";
    public string JobName { get => _jobName; set => SetField(ref _jobName, value); }

    private string _cameraId = "CAM01";
    public string CameraId { get => _cameraId; set => SetField(ref _cameraId, value); }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set => SetField(ref _isRunning, value); }

    private bool _isTriggerBusy;
    public bool IsTriggerBusy { get => _isTriggerBusy; set => SetField(ref _isTriggerBusy, value); }

    private string _statusMessage = "视觉系统就绪";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    // 统计
    private int _totalCount, _okCount, _ngCount;
    public int TotalCount { get => _totalCount; set => SetField(ref _totalCount, value); }
    public int OkCount { get => _okCount; set => SetField(ref _okCount, value); }
    public int NgCount { get => _ngCount; set => SetField(ref _ngCount, value); }
    public double YieldRate => TotalCount > 0 ? (double)OkCount / TotalCount * 100 : 0;

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand TriggerCommand { get; }
    public RelayCommand ClearCommand { get; }

    public VisionViewModel(IVisionService vision, VisionAnalysisService analysis)
    {
        _vision = vision;
        _analysis = analysis;

        _vision.ResultAvailable += (_, r) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Results.Insert(0, r);
                if (Results.Count > 200) Results.RemoveAt(Results.Count - 1);
                TotalCount++;
                if (r.ResultType == VisionResultType.OK) OkCount++;
                else if (r.ResultType == VisionResultType.NG) NgCount++;
                OnPropertyChanged(nameof(YieldRate));
                StatusMessage = $"检测结果：{r.ResultType}  置信度：{r.Confidence:P1}";
            });
        };

        StartCommand = new RelayCommand(_ => { _vision.Start(); IsRunning = true; StatusMessage = "视觉系统已启动"; });
        StopCommand = new RelayCommand(_ => { _vision.Stop(); IsRunning = false; StatusMessage = "视觉系统已停止"; });
        TriggerCommand = new RelayCommand(async _ => await Trigger(), _ => !IsTriggerBusy);
        ClearCommand = new RelayCommand(_ => { Results.Clear(); TotalCount = OkCount = NgCount = 0; OnPropertyChanged(nameof(YieldRate)); });
    }

    private async Task Trigger()
    {
        IsTriggerBusy = true;
        StatusMessage = "正在触发检测...";
        await _vision.TriggerAsync(JobName, CameraId);
        IsTriggerBusy = false;
    }
}
