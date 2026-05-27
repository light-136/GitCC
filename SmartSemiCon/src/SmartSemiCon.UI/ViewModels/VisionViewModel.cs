// ============================================================
// 文件：VisionViewModel.cs
// 用途：视觉系统页面ViewModel
// 设计思路：
//   管理视觉系统的核心功能：
//   1. 相机管理 — 打开/关闭相机，选择工作相机
//   2. 图像采集 — 手动触发采集
//   3. 算法执行 — 选择并执行视觉算法
//   4. 结果展示 — 显示检测结果和统计数据
// ============================================================

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;
using SmartSemiCon.Hardware.Vision;

namespace SmartSemiCon.UI.ViewModels
{
    /// <summary>
    /// 视觉系统页面ViewModel。
    /// </summary>
    public partial class VisionViewModel : ObservableObject
    {
        private readonly CameraManager _cameraManager;
        private readonly IVisionEngine _visionEngine;
        private readonly ILogService _logService;

        [ObservableProperty]
        private int _selectedCameraId;

        [ObservableProperty]
        private string _selectedAlgorithm = "TemplateMatch";

        [ObservableProperty]
        private string _inspectionResult = "等待检测...";

        [ObservableProperty]
        private int _totalInspections;

        [ObservableProperty]
        private int _passCount;

        [ObservableProperty]
        private int _failCount;

        [ObservableProperty]
        private double _passRate;

        // ---- 新增：连续检测模式 ----
        [ObservableProperty]
        private bool _isContinuousRunning;

        [ObservableProperty]
        private string _continuousStatus = "就绪";

        [ObservableProperty]
        private int _continuousInterval = 500;

        // ---- 新增：相机参数 ----
        [ObservableProperty]
        private double _exposureTime = 3000;

        [ObservableProperty]
        private double _gain = 1.0;

        private CancellationTokenSource? _continuousCts;

        /// <summary>可用算法列表</summary>
        public ObservableCollection<string> Algorithms { get; } = new()
        {
            "TemplateMatch", "BlobAnalysis", "OCR", "FindMark", "Measure", "DefectDetect"
        };

        /// <summary>检测结果历史</summary>
        public ObservableCollection<VisionResultEntry> ResultHistory { get; } = new();

        /// <summary>视觉日志</summary>
        public ObservableCollection<string> VisionLogs { get; } = new();

        public VisionViewModel(CameraManager cameraManager, IVisionEngine visionEngine, ILogService logService)
        {
            _cameraManager = cameraManager;
            _visionEngine = visionEngine;
            _logService = logService;
        }

        /// <summary>打开相机</summary>
        [RelayCommand]
        private async Task OpenCamera()
        {
            var camera = _cameraManager.GetCamera(SelectedCameraId);
            if (camera == null) return;
            await camera.OpenAsync();
            AddLog($"相机{SelectedCameraId} 已打开");
        }

        /// <summary>关闭相机</summary>
        [RelayCommand]
        private async Task CloseCamera()
        {
            var camera = _cameraManager.GetCamera(SelectedCameraId);
            if (camera == null) return;
            await camera.CloseAsync();
            AddLog($"相机{SelectedCameraId} 已关闭");
        }

        /// <summary>单次采集并检测</summary>
        [RelayCommand]
        private async Task CaptureAndInspect()
        {
            var camera = _cameraManager.GetCamera(SelectedCameraId);
            if (camera == null)
            {
                InspectionResult = "错误：未找到相机";
                return;
            }

            var image = await camera.CaptureAsync();
            if (image == null)
            {
                InspectionResult = "错误：采集失败";
                return;
            }

            var width = camera.Config.ImageWidth;
            var height = camera.Config.ImageHeight;

            VisionResult result = SelectedAlgorithm switch
            {
                "TemplateMatch" => await _visionEngine.TemplateMatchAsync(image, width, height, new object()),
                "BlobAnalysis" => await _visionEngine.BlobAnalysisAsync(image, width, height, 128.0),
                "OCR" => await _visionEngine.OcrAsync(image, width, height, new object()),
                "FindMark" => await _visionEngine.FindMarkAsync(image, width, height),
                "Measure" => await _visionEngine.MeasureAsync(image, width, height, new object()),
                "DefectDetect" => await _visionEngine.DefectDetectAsync(image, width, height, new object()),
                _ => await _visionEngine.FindMarkAsync(image, width, height)
            };

            TotalInspections++;
            if (result.IsSuccess) PassCount++;
            else FailCount++;
            PassRate = TotalInspections > 0 ? (double)PassCount / TotalInspections * 100 : 0;

            InspectionResult = result.IsSuccess
                ? $"成功 | 坐标({result.PixelX:F1}, {result.PixelY:F1}) 置信度={result.Score:F2} 耗时={result.ProcessTimeMs}ms{FormatExtraData(result)}"
                : $"失败 | {result.ErrorMessage}";

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                ResultHistory.Insert(0, new VisionResultEntry
                {
                    Time = DateTime.Now,
                    Algorithm = SelectedAlgorithm,
                    IsSuccess = result.IsSuccess,
                    Score = result.Score,
                    ProcessTimeMs = (int)result.ProcessTimeMs,
                    Detail = InspectionResult
                });
                if (ResultHistory.Count > 100) ResultHistory.RemoveAt(ResultHistory.Count - 1);
            });

            AddLog($"检测完成: {SelectedAlgorithm} → {(result.IsSuccess ? "通过" : "失败")}");
        }

        /// <summary>重置统计</summary>
        [RelayCommand]
        private void ResetStatistics()
        {
            TotalInspections = 0;
            PassCount = 0;
            FailCount = 0;
            PassRate = 0;
            ResultHistory.Clear();
            AddLog("统计数据已重置");
        }

        // ============== 新增：连续检测模式 ==============

        /// <summary>启动连续检测 — 自动循环采集+检测</summary>
        [RelayCommand]
        private async Task StartContinuous()
        {
            if (IsContinuousRunning) return;
            IsContinuousRunning = true;
            _continuousCts = new CancellationTokenSource();
            var token = _continuousCts.Token;

            AddLog($"连续检测启动: 间隔={ContinuousInterval}ms, 算法={SelectedAlgorithm}");

            int count = 0;
            while (!token.IsCancellationRequested)
            {
                count++;
                ContinuousStatus = $"运行中... 第{count}次";
                await CaptureAndInspect();

                try { await Task.Delay(ContinuousInterval, token); }
                catch (OperationCanceledException) { break; }
            }

            ContinuousStatus = $"已停止 (共{count}次)";
            IsContinuousRunning = false;
            AddLog($"连续检测停止: 共执行{count}次");
        }

        /// <summary>停止连续检测</summary>
        [RelayCommand]
        private void StopContinuous()
        {
            _continuousCts?.Cancel();
        }

        // ============== 新增：多相机同步采集 ==============

        /// <summary>4台相机同步采集并检测</summary>
        [RelayCommand]
        private async Task CaptureAllCameras()
        {
            AddLog("多相机同步采集开始");
            var tasks = new List<Task>();

            for (int camId = 0; camId < 4; camId++)
            {
                var camera = _cameraManager.GetCamera(camId);
                if (camera == null || !camera.IsOpened)
                {
                    await camera?.OpenAsync()!;
                }

                var id = camId;
                tasks.Add(Task.Run(async () =>
                {
                    var cam = _cameraManager.GetCamera(id)!;
                    var image = await cam.CaptureAsync();
                    if (image == null) return;

                    var result = await _visionEngine.FindMarkAsync(image, cam.Config.ImageWidth, cam.Config.ImageHeight);

                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        TotalInspections++;
                        if (result.IsSuccess) PassCount++; else FailCount++;
                        PassRate = TotalInspections > 0 ? (double)PassCount / TotalInspections * 100 : 0;

                        ResultHistory.Insert(0, new VisionResultEntry
                        {
                            Time = DateTime.Now,
                            Algorithm = $"FindMark[相机{id}]",
                            IsSuccess = result.IsSuccess,
                            Score = result.Score,
                            ProcessTimeMs = (int)result.ProcessTimeMs,
                            Detail = $"相机{id}: ({result.PixelX:F1},{result.PixelY:F1}) Score={result.Score:F2}"
                        });
                    });
                }));
            }

            await Task.WhenAll(tasks);
            AddLog($"多相机同步采集完成: {tasks.Count}台相机");
        }

        // ============== 新增：相机参数调节 ==============

        /// <summary>应用相机参数</summary>
        [RelayCommand]
        private async Task ApplyCameraParams()
        {
            var camera = _cameraManager.GetCamera(SelectedCameraId);
            if (camera == null) return;

            await camera.SetExposureAsync(ExposureTime);
            await camera.SetGainAsync(Gain);
            AddLog($"相机{SelectedCameraId} 参数更新: 曝光={ExposureTime}us 增益={Gain}");
        }

        // ============== 新增：详细结果展示 ==============

        /// <summary>增强版采集检测 — 显示 ExtraData 详情</summary>
        private string FormatExtraData(VisionResult result)
        {
            if (result.ExtraData.Count == 0) return "";
            var sb = new System.Text.StringBuilder(" | ");
            foreach (var kvp in result.ExtraData)
                sb.Append($"{kvp.Key}={kvp.Value} ");
            return sb.ToString();
        }

        private void AddLog(string message)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                VisionLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
                if (VisionLogs.Count > 200) VisionLogs.RemoveAt(VisionLogs.Count - 1);
            });
            _logService.Log(LogLevel.Info, "视觉系统", message);
        }
    }

    /// <summary>视觉检测结果条目 — 用于UI列表显示</summary>
    public class VisionResultEntry
    {
        public DateTime Time { get; set; }
        public string Algorithm { get; set; } = "";
        public bool IsSuccess { get; set; }
        public double Score { get; set; }
        public int ProcessTimeMs { get; set; }
        public string Detail { get; set; } = "";
    }
}
