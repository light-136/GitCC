using SmartMES.Core.Infrastructure;
using SmartMES.UI.Modules.MotionModule;
using SmartMES.Modules.VisionMotion;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartMES.UI.Modules.VisionMotionModule
{
    public class VisionMotionViewModel : ViewModelBase
    {
        private readonly MultiStationScheduler _scheduler = new();
        private readonly DispatcherTimer _refreshTimer;

        public ObservableCollection<StationItemViewModel> Stations { get; } = new();
        public ObservableCollection<AxisItemViewModel> Axes { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();

        private string _statusText = "就绪 - 点击启动生产";
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        private string _totalStats = "OK: 0  NG: 0";
        public string TotalStats { get => _totalStats; set => SetProperty(ref _totalStats, value); }

        private int _cycles = 5;
        public int Cycles
        {
            get => _cycles;
            set => SetProperty(ref _cycles, value < 1 ? 1 : value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (!SetProperty(ref _isRunning, value)) return;
                RefreshCommandState();
            }
        }

        public RelayCommand StartCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand ResetCommand { get; }

        public VisionMotionViewModel()
        {
            _scheduler.LogEmitted += (_, m) => AddLog(m);

            foreach (var st in _scheduler.Stations)
            {
                var vm = new StationItemViewModel(st);
                st.StateChanged += (_, _) => Application.Current?.Dispatcher.Invoke(vm.Refresh);
                st.CycleCompleted += (_, _) => Application.Current?.Dispatcher.Invoke(() =>
                {
                    vm.Refresh();
                    UpdateStats();
                });
                Stations.Add(vm);
            }

            foreach (var kv in _scheduler.Axes)
                Axes.Add(new AxisItemViewModel(kv.Value));

            StartCommand = new RelayCommand(async _ => await StartAsync(), _ => !_isRunning);
            StopCommand = new RelayCommand(_ => Stop(), _ => _isRunning);
            ResetCommand = new RelayCommand(_ => Reset(), _ => !_isRunning);

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _refreshTimer.Tick += (_, __) => { foreach (var a in Axes) a.Refresh(); };
            _refreshTimer.Start();

            RefreshCommandState();
        }

        private async Task StartAsync()
        {
            IsRunning = true;
            StatusText = $"生产中 ({_cycles} 周期/工位)...";
            AddLog($"[调度] 启动 {_scheduler.Stations.Count} 个工位，{_cycles} 周期");

            try
            {
                await _scheduler.RunProductionAsync(_cycles);
                StatusText = "生产完成";
            }
            catch (Exception ex)
            {
                StatusText = $"调度异常: {ex.Message}";
                AddLog($"[ERR] {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                UpdateStats();
            }
        }

        private void Stop()
        {
            _scheduler.Stop();
            IsRunning = false;
            StatusText = "已急停";
            AddLog("[调度] 已急停");
        }

        private void Reset()
        {
            _scheduler.Reset();
            foreach (var vm in Stations) vm.Refresh();
            foreach (var axis in Axes) axis.Refresh();
            UpdateStats();
            StatusText = "已复位";
            AddLog("[调度] 已复位");
        }

        private void UpdateStats()
        {
            int ok = _scheduler.Stations.Sum(s => s.OkCount);
            int ng = _scheduler.Stations.Sum(s => s.NgCount);
            int total = ok + ng;
            TotalStats = $"OK: {ok}  NG: {ng}  良品率: {(total == 0 ? "--" : $"{100.0 * ok / total:F1}%")}";
        }

        private void RefreshCommandState()
        {
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            ResetCommand.RaiseCanExecuteChanged();
        }

        private void AddLog(string msg)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Logs.Insert(0, msg);
                if (Logs.Count > 400) Logs.RemoveAt(Logs.Count - 1);
            });
        }
    }

    public class StationItemViewModel : ViewModelBase
    {
        private readonly StationScheduler _st;
        public string Name => _st.Name;
        public int OkCount => _st.OkCount;
        public int NgCount => _st.NgCount;
        public string StateText => _st.State.ToString();
        public string LogText => _st.Log;

        private static readonly SolidColorBrush BrushIdle = MakeFrozen(0x9A, 0xA0, 0xB4);
        private static readonly SolidColorBrush BrushPositioning = MakeFrozen(0x00, 0xD4, 0xFF);
        private static readonly SolidColorBrush BrushBusy = MakeFrozen(0xFF, 0xA5, 0x02);
        private static readonly SolidColorBrush BrushOk = MakeFrozen(0x39, 0xD3, 0x53);
        private static readonly SolidColorBrush BrushNg = MakeFrozen(0xFF, 0x47, 0x57);

        private SolidColorBrush _stateBrush = BrushIdle;
        public SolidColorBrush StateBrush
        {
            get => _stateBrush;
            private set => SetProperty(ref _stateBrush, value);
        }

        private static SolidColorBrush MakeFrozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public StationItemViewModel(StationScheduler st)
        {
            _st = st;
        }

        public void Refresh()
        {
            StateBrush = _st.State switch
            {
                StationState.Positioning => BrushPositioning,
                StationState.Capturing => BrushBusy,
                StationState.Detecting => BrushBusy,
                StationState.ActingOK => BrushOk,
                StationState.ActingNG => BrushNg,
                StationState.Error => BrushNg,
                StationState.Done => BrushOk,
                _ => BrushIdle
            };
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(OkCount));
            OnPropertyChanged(nameof(NgCount));
            OnPropertyChanged(nameof(LogText));
        }
    }
}
