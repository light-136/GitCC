using System.IO;
using SmartMES.Core.Infrastructure;
using SmartMES.Core.IO;
using SmartMES.Core.Plugin;
using SmartMES.Core.Recipe;
using SmartMES.Core.Safety;
using SmartMES.Core.Scheduler;
using SmartMES.Core.StateMachine;
using SmartMES.Core.Traceability;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartMES.UI.Modules.IndustrialModule
{
    public class IndustrialViewModel : ViewModelBase
    {
        private readonly TaskSchedulerService _scheduler  = new();
        private readonly StateMachineEngine   _sm;
        private readonly SimulatedIoDevice    _io         = new("SimIO-01");
        private readonly SafetyService        _safety     = new();
        private readonly RecipeService        _recipe     = new();
        private readonly TraceService         _trace      = new();
        private readonly PluginLoader         _plugins;
        private readonly CancellationTokenSource _cts     = new();
        private readonly DispatcherTimer      _uiTimer;
        private int _traceCounter = 1;

        // 状态机
        private string _smText = "Idle";
        public  string SmText { get => _smText; set => SetProperty(ref _smText, value); }
        private SolidColorBrush _smBrush = Frozen(0x9A,0xA0,0xB4);
        public  SolidColorBrush SmBrush  { get => _smBrush; set => SetProperty(ref _smBrush, value); }

        // 安全
        private bool _door = true;
        public  bool DoorClosed { get => _door; set { SetProperty(ref _door, value); _safety.DoorClosed = value; } }
        private bool _air = true;
        public  bool AirOk { get => _air; set { SetProperty(ref _air, value); _safety.AirPressureOk = value; } }
        private bool _eStop;
        public  bool EStopActive { get => _eStop; set => SetProperty(ref _eStop, value); }

        // 配方
        public ObservableCollection<string>        RecipeNames  { get; } = new();
        public ObservableCollection<RecipeParamRow> RecipeParams { get; } = new();
        private string? _selRecipe;
        public  string? SelectedRecipeName { get => _selRecipe; set => SetProperty(ref _selRecipe, value); }
        private string? _activeRecipe;
        public  string? ActiveRecipeName { get => _activeRecipe; set => SetProperty(ref _activeRecipe, value); }

        // 调度/IO/追溯/插件
        public ObservableCollection<SchedulerRow>  Tasks    { get; } = new();
        public ObservableCollection<IoRow>         IoItems  { get; } = new();
        public ObservableCollection<TraceRow>      Traces   { get; } = new();
        public ObservableCollection<string>        Logs     { get; } = new();

        // 命令
        public RelayCommand StartCmd    { get; }
        public RelayCommand StopCmd     { get; }
        public RelayCommand PauseCmd    { get; }
        public RelayCommand AlarmCmd    { get; }
        public RelayCommand ResetCmd    { get; }
        public RelayCommand EStopCmd    { get; }
        public RelayCommand ClearEStopCmd { get; }
        public RelayCommand ActivateRecipeCmd { get; }
        public RelayCommand AddTraceCmd { get; }
        public RelayCommand ScanPluginsCmd { get; }
        public RelayCommand ToggleDoCmd { get; }

        /// <summary>
        /// 自动补齐：IndustrialViewModel 方法说明。
        /// </summary>
        public IndustrialViewModel()
        {
            _sm = StateMachineEngine.BuildStandard("主设备");
            _sm.StateChanged += OnSmChanged;
            _plugins = new PluginLoader(new DefaultPluginContext(m => AddLog($"[Plugin] {m}")));

            _scheduler.AddTask(ScheduledTaskFactory.Periodic("IO轮询",
                TimeSpan.FromSeconds(1), _ => { RefreshIo(); return Task.CompletedTask; }, TaskPriority.High));
            _scheduler.AddTask(ScheduledTaskFactory.Periodic("心跳",
                TimeSpan.FromSeconds(5), _ => { AddLog("[Scheduler] 心跳 ✓"); return Task.CompletedTask; }, TaskPriority.Low));
            _ = Task.Run(() => _scheduler.StartAsync(_cts.Token));

            _safety.EStopTriggered   += (_, e) => { EStopActive = true;  AddLog($"🛑 {e.Message}"); };
            _safety.InterlockBlocked += (_, e) => AddLog($"🔒 互锁: {e.Message}");

            _recipe.RecipeActivated += (_, r) =>
            {
                ActiveRecipeName = r.Name;
                Application.Current?.Dispatcher.BeginInvoke(() => LoadRecipeParams(r));
                AddLog($"📋 配方切换: {r.Name}");
            };

            StartCmd          = new RelayCommand(_ => Fire("Start",    "StartDevice"));
            StopCmd           = new RelayCommand(_ => Fire("Stop",     "*"));
            PauseCmd          = new RelayCommand(_ => Fire("Pause",    "*"));
            AlarmCmd          = new RelayCommand(_ => Fire("Alarm",    "*"));
            ResetCmd          = new RelayCommand(_ => { Fire("Reset","*"); Fire("ResetDone","*"); });
            EStopCmd          = new RelayCommand(_ => { _safety.TriggerEStop("手动急停"); _sm.ForceState(MachineState.Alarm); });
            ClearEStopCmd     = new RelayCommand(_ => { _safety.ResetEStop(); EStopActive = false; AddLog("急停已解除"); });
            ActivateRecipeCmd = new RelayCommand(_ => { if (_selRecipe!=null) _recipe.Activate(_selRecipe); });
            AddTraceCmd       = new RelayCommand(_ => DoTrace());
            ScanPluginsCmd    = new RelayCommand(_ => DoScanPlugins());
            ToggleDoCmd       = new RelayCommand(_ => { _io.WriteOutput(100,!_io.ReadInput(100)); AddLog("[IO] DO00 切换"); });

            foreach (var r in _recipe.GetAll()) RecipeNames.Add(r.Name);
            SelectedRecipeName = RecipeNames.FirstOrDefault();
            LoadRecipeParams(_recipe.ActiveRecipe);
            ActiveRecipeName = _recipe.ActiveRecipe?.Name;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _uiTimer.Tick += (_, __) => RefreshTasks();
            _uiTimer.Start();
            AddLog("✅ 工业级模块全部初始化完成");
        }

        /// <summary>
        /// 自动补齐：Fire 方法说明。
        /// </summary>
        private void Fire(string trigger, string safetyOp)
        {
            if (!_safety.IsSafeToOperate(safetyOp)) return;
            if (!_sm.Fire(trigger)) AddLog($"[SM] 触发失败: {trigger}（当前: {_smText}）");
        }

        /// <summary>
        /// 自动补齐：OnSmChanged 方法说明。
        /// </summary>
        private void OnSmChanged(object? s, StateChangedArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                SmText  = e.To.ToString();
                SmBrush = e.To switch
                {
                    MachineState.Running   => Frozen(0x39,0xD3,0x53),
                    MachineState.Alarm     => Frozen(0xFF,0x47,0x57),
                    MachineState.Paused    => Frozen(0xFF,0xA5,0x02),
                    MachineState.Resetting => Frozen(0x00,0xD4,0xFF),
                    MachineState.Error     => Frozen(0xFF,0x00,0x00),
                    _                      => Frozen(0x9A,0xA0,0xB4)
                };
                AddLog($"[SM] {e.From} --{e.Trigger}--> {e.To}");
            });
        }

        /// <summary>
        /// 自动补齐：RefreshIo 方法说明。
        /// </summary>
        private void RefreshIo()
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                IoItems.Clear();
                foreach (var ch in _io.GetChannels().Take(12))
                    IoItems.Add(new IoRow
                    {
                        Name  = ch.Name,
                        Type  = ch.Type.ToString()[0..2],
                        Value = (ch.Type==IoChannelType.DigitalInput||ch.Type==IoChannelType.DigitalOutput)
                            ? (ch.Value?"ON":"OFF") : ch.AnalogValue.ToString("F1")
                    });
            });
        }

        /// <summary>
        /// 自动补齐：RefreshTasks 方法说明。
        /// </summary>
        private void RefreshTasks()
        {
            Tasks.Clear();
            foreach (var t in _scheduler.GetAllTasks())
                Tasks.Add(new SchedulerRow
                {
                    Name=t.Name, Priority=t.Priority.ToString(),
                    State=t.State.ToString(), Count=t.RunCount,
                    Next=t.NextRunTime?.ToString("HH:mm:ss")??"--"
                });
        }

        /// <summary>
        /// 自动补齐：LoadRecipeParams 方法说明。
        /// </summary>
        private void LoadRecipeParams(RecipeModel? r)
        {
            RecipeParams.Clear();
            if (r==null) return;
            foreach (var p in r.Parameters)
                RecipeParams.Add(new RecipeParamRow{Name=p.Name,Value=p.Value,Unit=p.Unit});
        }

        /// <summary>
        /// 自动补齐：DoTrace 方法说明。
        /// </summary>
        private void DoTrace()
        {
            string sn = $"SN{DateTime.Now:yyyyMMddHHmmss}{_traceCounter++:D3}";
            _trace.StartTrace(sn, $"LOT-{DateTime.Today:yyyyMMdd}", "PA001");
            var proc = _trace.StartProcess(sn, "视觉检测", "ST-01", "CAM-01");
            bool ok  = new Random().NextDouble() > 0.2;
            _trace.EndProcess(proc.Id, ok, ok?"OK":"NG");
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                Traces.Insert(0, new TraceRow{SN=sn,Result=ok?"OK":"NG",Time=DateTime.Now.ToString("HH:mm:ss")});
                if (Traces.Count>50) Traces.RemoveAt(50);
            });
            AddLog($"[Trace] {sn} {(ok?"OK":"NG")}");
        }

        /// <summary>
        /// 自动补齐：DoScanPlugins 方法说明。
        /// </summary>
        private void DoScanPlugins()
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            Directory.CreateDirectory(dir);
            var list = _plugins.ScanDirectory(dir);
            AddLog($"[Plugin] 扫描完成，发现 {list.Count} 个插件");
            if (list.Count==0) AddLog("[Plugin] 将.dll插件放入plugins目录后重新扫描");
        }

        /// <summary>
        /// 自动补齐：AddLog 方法说明。
        /// </summary>
        private void AddLog(string msg)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
                if (Logs.Count>200) Logs.RemoveAt(Logs.Count-1);
            });
        }

        /// <summary>
        /// 自动补齐：Frozen 方法说明。
        /// </summary>
        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        { var b2=new SolidColorBrush(Color.FromRgb(r,g,b)); b2.Freeze(); return b2; }
    }

    public class SchedulerRow  { public string Name{get;set;}=""; public string Priority{get;set;}=""; public string State{get;set;}=""; public int Count{get;set;} public string Next{get;set;}=""; }
    public class IoRow         { public string Name{get;set;}=""; public string Type{get;set;}=""; public string Value{get;set;}=""; }
    public class RecipeParamRow{ public string Name{get;set;}=""; public string Value{get;set;}=""; public string Unit{get;set;}=""; }
    public class TraceRow      { public string SN{get;set;}="";   public string Result{get;set;}=""; public string Time{get;set;}=""; }
}
