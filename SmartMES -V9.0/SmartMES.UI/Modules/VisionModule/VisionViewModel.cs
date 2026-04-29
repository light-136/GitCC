using SmartMES.Core.Infrastructure;
using SmartMES.Modules.Vision;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SmartMES.UI.Modules.VisionModule
{
    public class VisionViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _cameraTimer;
        private bool _cameraRunning;
        private int _okCount;
        private int _ngCount;

        private static readonly SolidColorBrush BrushOK;
        private static readonly SolidColorBrush BrushNG;
        private static readonly SolidColorBrush BrushIdle;

        static VisionViewModel()
        {
            BrushOK = new SolidColorBrush(Color.FromRgb(0x39, 0xD3, 0x53));
            BrushOK.Freeze();
            BrushNG = new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57));
            BrushNG.Freeze();
            BrushIdle = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xB4));
            BrushIdle.Freeze();
        }

        private WriteableBitmap? _liveImage;
        public WriteableBitmap? LiveImage
        {
            get => _liveImage;
            set
            {
                if (!SetProperty(ref _liveImage, value)) return;
                InspectCommand.RaiseCanExecuteChanged();
            }
        }

        private WriteableBitmap? _processedImage;
        public WriteableBitmap? ProcessedImage { get => _processedImage; set => SetProperty(ref _processedImage, value); }

        private string _resultText = "等待检测...";
        public string ResultText { get => _resultText; set => SetProperty(ref _resultText, value); }

        private SolidColorBrush _resultBrush = BrushIdle;
        public SolidColorBrush ResultBrush { get => _resultBrush; set => SetProperty(ref _resultBrush, value); }

        private string _statsText = "OK: 0  NG: 0  良品率: --";
        public string StatsText { get => _statsText; set => SetProperty(ref _statsText, value); }

        private bool _showEdge;
        public bool ShowEdge { get => _showEdge; set => SetProperty(ref _showEdge, value); }

        private int _threshold = 80;
        public int Threshold { get => _threshold; set => SetProperty(ref _threshold, value); }

        private double _defectRatio = 0.005;
        public double DefectRatio
        {
            get => _defectRatio;
            set
            {
                if (!SetProperty(ref _defectRatio, value)) return;
                OnPropertyChanged(nameof(DefectRatioText));
            }
        }
        public string DefectRatioText => $"限值: {_defectRatio:P1}";

        public ObservableCollection<string> Logs { get; } = new();

        public RelayCommand StartCameraCommand { get; }
        public RelayCommand StopCameraCommand { get; }
        public RelayCommand CaptureCommand { get; }
        public RelayCommand InspectCommand { get; }
        public RelayCommand GenerateNGCommand { get; }
        public RelayCommand PresetFastCommand { get; }
        public RelayCommand PresetBalanceCommand { get; }
        public RelayCommand PresetStrictCommand { get; }

        public VisionViewModel()
        {
            StartCameraCommand = new RelayCommand(_ => StartCamera(), _ => !_cameraRunning);
            StopCameraCommand = new RelayCommand(_ => StopCamera(), _ => _cameraRunning);
            CaptureCommand = new RelayCommand(_ => Capture());
            InspectCommand = new RelayCommand(_ => Inspect(), _ => _liveImage != null);
            GenerateNGCommand = new RelayCommand(_ => GenerateNG());

            PresetFastCommand = new RelayCommand(_ => ApplyPreset(95, 0.012, "快速检测"));
            PresetBalanceCommand = new RelayCommand(_ => ApplyPreset(80, 0.006, "均衡检测"));
            PresetStrictCommand = new RelayCommand(_ => ApplyPreset(65, 0.003, "精检模式"));

            _cameraTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _cameraTimer.Tick += (_, __) => RefreshFrame();
        }

        private void StartCamera()
        {
            _cameraRunning = true;
            _cameraTimer.Start();
            RefreshFrame();
            StartCameraCommand.RaiseCanExecuteChanged();
            StopCameraCommand.RaiseCanExecuteChanged();
            AddLog("📷 模拟摄像头已启动 (640×480 @~8fps)");
        }

        private void StopCamera()
        {
            _cameraRunning = false;
            _cameraTimer.Stop();
            StartCameraCommand.RaiseCanExecuteChanged();
            StopCameraCommand.RaiseCanExecuteChanged();
            AddLog("📷 摄像头已停止");
        }

        private void RefreshFrame()
        {
            try
            {
                bool hasDefect = Random.Shared.NextDouble() < 0.15;
                LiveImage = VisionEngine.GenerateWorkpieceImage(640, 480, hasDefect);
            }
            catch (Exception ex)
            {
                AddLog($"[ERR] 帧刷新失败: {ex.Message}");
            }
        }

        private void Capture()
        {
            try
            {
                LiveImage = VisionEngine.GenerateWorkpieceImage(640, 480, Random.Shared.NextDouble() < 0.3);
                AddLog("📸 已抓取帧");
            }
            catch (Exception ex)
            {
                AddLog($"[ERR] 抓帧失败: {ex.Message}");
            }
        }

        private void GenerateNG()
        {
            try
            {
                LiveImage = VisionEngine.GenerateWorkpieceImage(640, 480, hasDefect: true);
                AddLog("⚠ 已生成含缺陷图像");
            }
            catch (Exception ex)
            {
                AddLog($"[ERR] {ex.Message}");
            }
        }

        private void Inspect()
        {
            if (_liveImage == null) return;

            try
            {
                var src = _showEdge ? VisionEngine.SobelEdge(_liveImage) : _liveImage;
                var result = VisionEngine.Inspect(src, _threshold, _defectRatio);

                ProcessedImage = result.ProcessedImage;

                bool isOk = result.Result == DetectionResult.OK;
                if (isOk) _okCount++; else _ngCount++;

                ResultText = result.Message;
                ResultBrush = isOk ? BrushOK : BrushNG;

                int total = _okCount + _ngCount;
                StatsText = $"OK: {_okCount}  NG: {_ngCount}  良品率: {(total == 0 ? "--" : $"{100.0 * _okCount / total:F1}%")}";

                AddLog($"[{(isOk ? "OK" : "NG")}] {result.Message}  处理: {result.ProcessTime.TotalMilliseconds:F0}ms");
                foreach (var d in result.Defects)
                    AddLog($"  - 缺陷[{d.Type}] @({d.X},{d.Y}) {d.Width}×{d.Height}px 置信:{d.Confidence:P0}");
            }
            catch (Exception ex)
            {
                AddLog($"[ERR] 检测失败: {ex.Message}");
            }
        }

        private void ApplyPreset(int threshold, double defectRatio, string name)
        {
            Threshold = threshold;
            DefectRatio = defectRatio;
            AddLog($"[预设] 已切换{name}: 阈值={threshold}, 限值={defectRatio:P2}");
        }

        private void AddLog(string msg)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
                if (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
            });
        }
    }
}
