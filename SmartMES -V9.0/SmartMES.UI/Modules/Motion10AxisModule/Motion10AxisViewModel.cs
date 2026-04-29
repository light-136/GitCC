using SmartMES.Core.Infrastructure;
using SmartMES.UI.Modules.MotionModule;
using SmartMES.Modules.MotionControl;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace SmartMES.UI.Modules.Motion10AxisModule
{
    public class Motion10AxisViewModel : ViewModelBase
    {
        private readonly TenAxisController _ctrl = new();
        private readonly DispatcherTimer _refreshTimer;
        private CancellationTokenSource _cts = new();

        public ObservableCollection<AxisItemViewModel> Axes { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();

        private string _gcode = TenAxisController.GetSampleProgram();
        public string GCode { get => _gcode; set => SetProperty(ref _gcode, value); }

        private string _statusText = "就绪 - 可修改G代码后执行";
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        private int _cycles = 3;
        public int Cycles
        {
            get => _cycles;
            set => SetProperty(ref _cycles, value < 1 ? 1 : value);
        }

        private bool _isLoopMode = true;
        public bool IsLoopMode
        {
            get => _isLoopMode;
            set
            {
                if (SetProperty(ref _isLoopMode, value))
                    OnPropertyChanged(nameof(ModeText));
            }
        }

        private int _axisPanelColumns = 5;
        public int AxisPanelColumns
        {
            get => _axisPanelColumns;
            set => SetProperty(ref _axisPanelColumns, value);
        }

        private int _axisPanelRows = 2;
        public int AxisPanelRows
        {
            get => _axisPanelRows;
            set => SetProperty(ref _axisPanelRows, value);
        }

        private string _layoutModeText = "5×2 横向布局";
        public string LayoutModeText
        {
            get => _layoutModeText;
            set => SetProperty(ref _layoutModeText, value);
        }

        public string ModeText => IsLoopMode ? "循环执行" : "单次执行";

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }

        private int _executedLines;
        public int ExecutedLines { get => _executedLines; set => SetProperty(ref _executedLines, value); }

        private string _elapsedText = "0.0s";
        public string ElapsedText { get => _elapsedText; set => SetProperty(ref _elapsedText, value); }

        public RelayCommand HomeAllCommand { get; }
        public RelayCommand RunProgramCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand ResetCommand { get; }
        public RelayCommand LoadSampleCommand { get; }
        public RelayCommand Layout52Command { get; }
        public RelayCommand Layout25Command { get; }

        public Motion10AxisViewModel()
        {
            _ctrl.MessageLogged += (_, msg) => AddLog(msg);

            foreach (var kv in _ctrl.Axes)
                Axes.Add(new AxisItemViewModel(kv.Value));

            HomeAllCommand = new RelayCommand(async _ => await SafeAsync(() => _ctrl.HomeAllAsync()), _ => !_isRunning);
            RunProgramCommand = new RelayCommand(async _ => await RunProgramAsync(), _ => !_isRunning);
            StopCommand = new RelayCommand(_ => Stop(), _ => _isRunning);
            ResetCommand = new RelayCommand(_ =>
            {
                _ctrl.ResetAll();
                IsRunning = false;
                StatusText = "已复位";
                ExecutedLines = 0;
                ElapsedText = "0.0s";
                AddLog("[复位] 所有轴已复位");
                RefreshCommandState();
            }, _ => !_isRunning);
            LoadSampleCommand = new RelayCommand(_ =>
            {
                GCode = TenAxisController.GetSampleProgram();
                AddLog("[示例] 已加载示例G代码程序");
            }, _ => !_isRunning);

            Layout52Command = new RelayCommand(_ =>
            {
                AxisPanelColumns = 5;
                AxisPanelRows = 2;
                LayoutModeText = "5×2 横向布局";
                AddLog("[布局] 已切换为5×2横向布局");
            });

            Layout25Command = new RelayCommand(_ =>
            {
                AxisPanelColumns = 2;
                AxisPanelRows = 5;
                LayoutModeText = "2×5 纵向布局";
                AddLog("[布局] 已切换为2×5纵向布局");
            });

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _refreshTimer.Tick += (_, __) => { foreach (var a in Axes) a.Refresh(); };
            _refreshTimer.Start();

            AddLog("✔ 10轴运动控制器已初始化 (X Y Z A B C U V W S)");
            RefreshCommandState();
        }

        private async Task RunProgramAsync()
        {
            _cts = new CancellationTokenSource();
            IsRunning = true;
            RefreshCommandState();

            StatusText = $"程序执行中... {ModeText}";
            ExecutedLines = 0;
            ElapsedText = "0.0s";

            int targetCycles = IsLoopMode ? Cycles : 1;
            var start = DateTime.Now;
            try
            {
                for (int i = 1; i <= targetCycles; i++)
                {
                    if (_cts.IsCancellationRequested)
                        break;

                    StatusText = targetCycles == 1
                        ? "执行单次程序..."
                        : $"执行周期 {i}/{targetCycles}...";

                    var result = await _ctrl.RunGCodeAsync(_gcode, _cts.Token);
                    ExecutedLines += result.ExecutedLines;
                    AddLog(targetCycles == 1
                        ? $"[单次] {result.Message}"
                        : $"[周期 {i}] {result.Message}");

                    if (!result.Success)
                    {
                        StatusText = $"执行终止: {result.Message}";
                        break;
                    }
                }

                ElapsedText = $"{(DateTime.Now - start).TotalSeconds:F1}s";
                if (!_cts.IsCancellationRequested)
                {
                    StatusText = $"执行完成 | 累计行数: {ExecutedLines} | 用时: {ElapsedText}";
                    AddLog($"[完成] {StatusText}");
                }
            }
            catch (Exception ex)
            {
                StatusText = $"执行出错: {ex.Message}";
                AddLog($"[ERR] {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                RefreshCommandState();
            }
        }

        private void Stop()
        {
            _cts.Cancel();
            _ctrl.EmergencyStop();
            IsRunning = false;
            StatusText = "已急停";
            AddLog("[急停] 紧急停止指令已发送");
            RefreshCommandState();
        }

        private async Task SafeAsync(Func<Task> fn)
        {
            try
            {
                IsRunning = true;
                RefreshCommandState();
                await fn();
                StatusText = "全部回零完成";
            }
            catch (Exception ex)
            {
                AddLog($"[ERR] {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                RefreshCommandState();
            }
        }

        private void RefreshCommandState()
        {
            HomeAllCommand.RaiseCanExecuteChanged();
            RunProgramCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            ResetCommand.RaiseCanExecuteChanged();
            LoadSampleCommand.RaiseCanExecuteChanged();
        }

        private void AddLog(string msg)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
                if (Logs.Count > 400) Logs.RemoveAt(Logs.Count - 1);
            });
        }
    }
}
