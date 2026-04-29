using SmartMES.Core.Infrastructure;
using SmartMES.Modules.MotionControl;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartMES.UI.Modules.MotionModule
{
    public class MotionViewModel : ViewModelBase
    {
        private readonly MultiAxisController _controller = new();
        private readonly DispatcherTimer _refreshTimer;

        public ObservableCollection<AxisItemViewModel> Axes { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();

        private double _globalSpeed = 200;
        public double GlobalSpeed
        {
            get => _globalSpeed;
            set
            {
                SetProperty(ref _globalSpeed, value);
                foreach (var a in _controller.Axes.Values)
                    a.Velocity = value;
                OnPropertyChanged(nameof(GlobalSpeedText));
            }
        }
        public string GlobalSpeedText => $"{_globalSpeed:F0} mm/s";

        private double _globalAccel = 600;
        public double GlobalAccel
        {
            get => _globalAccel;
            set
            {
                SetProperty(ref _globalAccel, value);
                foreach (var a in _controller.Axes.Values)
                    a.Acceleration = value;
                OnPropertyChanged(nameof(GlobalAccelText));
            }
        }
        public string GlobalAccelText => $"{_globalAccel:F0} mm/s²";

        private string _interpolStatus = "就绪";
        public string InterpolStatus { get => _interpolStatus; set => SetProperty(ref _interpolStatus, value); }

        private int _axisCount;
        public int AxisCount { get => _axisCount; set => SetProperty(ref _axisCount, value); }

        public RelayCommand HomeAllCommand { get; }
        public RelayCommand MoveAllCommand { get; }
        public RelayCommand PauseAllCommand { get; }
        public RelayCommand ResumeAllCommand { get; }
        public RelayCommand EStopCommand { get; }
        public RelayCommand ResetAllCommand { get; }
        public RelayCommand LinearInterpCommand { get; }
        public RelayCommand MoveToLoadPoseCommand { get; }
        public RelayCommand MoveToWorkPoseCommand { get; }
        public RelayCommand MoveToSafePoseCommand { get; }

        public MotionViewModel()
        {
            _controller.AddAxis("X", velocity: 200, accel: 800);
            _controller.AddAxis("Y", velocity: 150, accel: 600);
            _controller.AddAxis("Z", velocity: 100, accel: 400);
            _controller.MessageLogged += (_, msg) => AddLog(msg);

            foreach (var kv in _controller.Axes)
                Axes.Add(new AxisItemViewModel(kv.Value));

            AxisCount = Axes.Count;

            HomeAllCommand = new RelayCommand(async _ => await SafeAsync(() => _controller.HomeAllAsync()));
            MoveAllCommand = new RelayCommand(_ => MoveRandom());
            PauseAllCommand = new RelayCommand(_ =>
            {
                foreach (var a in _controller.Axes.Values) a.Pause();
                AddLog("[全局] 所有轴已暂停");
            });
            ResumeAllCommand = new RelayCommand(_ =>
            {
                foreach (var a in _controller.Axes.Values) a.Resume();
                AddLog("[全局] 所有轴已恢复");
            });
            EStopCommand = new RelayCommand(_ =>
            {
                _controller.EmergencyStop();
                InterpolStatus = "急停";
                AddLog("🛑 紧急停止");
            });
            ResetAllCommand = new RelayCommand(_ =>
            {
                _controller.ResetAll();
                InterpolStatus = "已复位";
                AddLog("[全局] 所有轴已复位");
            });
            LinearInterpCommand = new RelayCommand(async _ => await SafeAsync(LinearInterpAsync));

            MoveToLoadPoseCommand = new RelayCommand(async _ => await SafeAsync(() => MovePresetAsync(
                new Dictionary<string, double> { ["X"] = 80, ["Y"] = 45, ["Z"] = 65 }, "上料位")));
            MoveToWorkPoseCommand = new RelayCommand(async _ => await SafeAsync(() => MovePresetAsync(
                new Dictionary<string, double> { ["X"] = 220, ["Y"] = 180, ["Z"] = 25 }, "加工位")));
            MoveToSafePoseCommand = new RelayCommand(async _ => await SafeAsync(() => MovePresetAsync(
                new Dictionary<string, double> { ["X"] = 20, ["Y"] = 20, ["Z"] = 120 }, "安全位")));

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _refreshTimer.Tick += (_, __) => { foreach (var a in Axes) a.Refresh(); };
            _refreshTimer.Start();

            AddLog("✔ 三轴运动控制器已初始化 (X/Y/Z)");
        }

        private async Task SafeAsync(Func<Task> fn)
        {
            try { await fn(); }
            catch (Exception ex) { AddLog($"[ERR] {ex.Message}"); }
        }

        private void MoveRandom()
        {
            var rnd = new Random();
            foreach (var a in _controller.Axes.Values)
                if (a.State == AxisState.Idle)
                    a.MoveTo(Math.Round(rnd.NextDouble() * 400, 1));
            AddLog("[全局] 随机移动指令已下发");
        }

        private async Task LinearInterpAsync()
        {
            var rnd = new Random();
            var pt = new InterpolationPoint
            {
                FeedRate = _globalSpeed,
                AxisTargets = _controller.Axes.Keys
                    .ToDictionary(k => k, k => Math.Round(rnd.NextDouble() * 300, 1))
            };
            InterpolStatus = $"插补中: {string.Join(", ", pt.AxisTargets.Select(kv => $"{kv.Key}={kv.Value:F1}mm"))}";
            await _controller.LinearInterpolateAsync(pt);
            InterpolStatus = "插补完成";
        }

        private async Task MovePresetAsync(Dictionary<string, double> targets, string poseName)
        {
            InterpolStatus = $"前往{poseName}...";
            await _controller.LinearInterpolateAsync(new InterpolationPoint
            {
                FeedRate = _globalSpeed,
                AxisTargets = targets
            });
            InterpolStatus = $"已到达{poseName}";
            AddLog($"[预设] 已到达{poseName}: {string.Join(", ", targets.Select(kv => $"{kv.Key}={kv.Value:F1}"))}");
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

    public class AxisItemViewModel : ViewModelBase
    {
        private readonly AxisController _axis;
        public string Name => _axis.AxisName;

        private static readonly SolidColorBrush BrushIdle;
        private static readonly SolidColorBrush BrushRunning;
        private static readonly SolidColorBrush BrushError;
        private static readonly SolidColorBrush BrushPaused;
        private static readonly SolidColorBrush BrushHoming;

        static AxisItemViewModel()
        {
            BrushIdle = MakeFrozen(0x9A, 0xA0, 0xB4);
            BrushRunning = MakeFrozen(0x39, 0xD3, 0x53);
            BrushError = MakeFrozen(0xFF, 0x47, 0x57);
            BrushPaused = MakeFrozen(0xFF, 0xA5, 0x02);
            BrushHoming = MakeFrozen(0x00, 0xD4, 0xFF);
        }

        private static SolidColorBrush MakeFrozen(byte r, byte g, byte b)
        {
            var b2 = new SolidColorBrush(Color.FromRgb(r, g, b));
            b2.Freeze();
            return b2;
        }

        private AxisState _state;
        public AxisState State { get => _state; private set => SetProperty(ref _state, value); }
        public string StateText => _state.ToString();

        private SolidColorBrush _stateBrush = BrushIdle;
        public SolidColorBrush StateBrush { get => _stateBrush; private set => SetProperty(ref _stateBrush, value); }

        private string _positionText = "0.00 mm";
        public string PositionText { get => _positionText; private set => SetProperty(ref _positionText, value); }

        private string _targetText = "→ 0.00 mm";
        public string TargetText { get => _targetText; private set => SetProperty(ref _targetText, value); }

        private double _positionClamped = 0.0;
        public double PositionClamped { get => _positionClamped; private set => SetProperty(ref _positionClamped, value); }

        private double _moveTarget = 100.0;
        public double MoveTarget { get => _moveTarget; set => SetProperty(ref _moveTarget, value); }

        public RelayCommand HomeCommand { get; }
        public RelayCommand PauseCommand { get; }
        public RelayCommand ResumeCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand ResetCommand { get; }
        public RelayCommand MoveCommand { get; }

        public AxisItemViewModel(AxisController axis)
        {
            _axis = axis;
            HomeCommand = new RelayCommand(_ => _axis.Home());
            PauseCommand = new RelayCommand(_ => _axis.Pause());
            ResumeCommand = new RelayCommand(_ => _axis.Resume());
            StopCommand = new RelayCommand(_ => _axis.Stop());
            ResetCommand = new RelayCommand(_ => _axis.Reset());
            MoveCommand = new RelayCommand(_ => _axis.MoveTo(_moveTarget));
        }

        public void Refresh()
        {
            double pos = Math.Round(_axis.Position, 2);
            double tgt = Math.Round(_axis.TargetPosition, 2);
            PositionText = $"{pos:F2} mm";
            TargetText = $"→ {tgt:F2} mm";
            PositionClamped = Math.Max(0, Math.Min(500, pos));

            var s = _axis.State;
            if (s == _state) return;

            State = s;
            StateBrush = s switch
            {
                AxisState.Running => BrushRunning,
                AxisState.Error => BrushError,
                AxisState.Paused => BrushPaused,
                AxisState.Homing => BrushHoming,
                _ => BrushIdle
            };
            OnPropertyChanged(nameof(StateText));
        }
    }
}
