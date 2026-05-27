// ============================================================
// 文件：MainViewModel.cs
// 用途：主窗口ViewModel — 管理导航、状态显示、全局操作
// 设计思路：
//   使用 CommunityToolkit.Mvvm 简化MVVM开发。
//   主ViewModel管理：
//   1. 导航 — 切换不同功能页面
//   2. 设备状态 — 实时显示设备运行状态
//   3. 报警计数 — 实时显示活跃报警数量
//   4. 用户信息 — 当前登录用户
//   5. 全局操作 — 急停、报警复位等
// ============================================================

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Events;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;
using SmartSemiCon.Application.Alarm;
using SmartSemiCon.Application.StateMachine;
using SmartSemiCon.Application.TaskScheduler;
using SmartSemiCon.Hardware.Motion.Axis;
using SmartSemiCon.Hardware.Vision;
using SmartSemiCon.Hardware.VisionMotion;

namespace SmartSemiCon.UI.ViewModels
{
    /// <summary>
    /// 导航页面枚举。
    /// </summary>
    public enum NavigationPage
    {
        Dashboard,      // 设备总览
        Motion,         // 运动控制
        Vision,         // 视觉系统
        SecsGem,        // SECS/GEM
        Communication,  // 通讯监控
        Alarm,          // 报警管理
        Recipe,         // 配方管理
        Log,            // 日志查看
        Settings,       // 系统设置
        User            // 用户管理
    }

    /// <summary>
    /// 主窗口ViewModel。
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly IEventBus _eventBus;
        private readonly IAlarmService _alarmService;
        private readonly ILogService _logService;
        private readonly IUserService _userService;
        private readonly DeviceStateMachine _stateMachine;
        private readonly AxisManager _axisManager;
        private readonly CameraManager _cameraManager;
        private readonly IndustrialTaskScheduler _taskScheduler;
        private readonly VisionMotionCoordinator _coordinator;
        private readonly IVisionEngine _visionEngine;

        // ---- 子页面ViewModel ----

        /// <summary>运动控制ViewModel</summary>
        public MotionViewModel MotionVm { get; }

        /// <summary>视觉系统ViewModel</summary>
        public VisionViewModel VisionVm { get; }

        /// <summary>SECS/GEM ViewModel</summary>
        public SecsGemViewModel SecsGemVm { get; }

        /// <summary>通讯监控ViewModel</summary>
        public CommunicationViewModel CommVm { get; }

        /// <summary>报警管理ViewModel</summary>
        public AlarmViewModel AlarmVm { get; }

        /// <summary>配方管理ViewModel</summary>
        public RecipeViewModel RecipeVm { get; }

        /// <summary>日志查看ViewModel</summary>
        public LogViewModel LogVm { get; }

        /// <summary>系统设置ViewModel</summary>
        public SettingsViewModel SettingsVm { get; }

        /// <summary>用户管理ViewModel</summary>
        public UserViewModel UserVm { get; }

        // ---- 绑定属性 ----

        [ObservableProperty]
        private string _title = "SmartSemiCon — 半导体设备工业控制平台 V1.0";

        [ObservableProperty]
        private NavigationPage _currentPage = NavigationPage.Dashboard;

        [ObservableProperty]
        private string _deviceStateText = "空闲";

        [ObservableProperty]
        private string _deviceStateColor = "#4CAF50";

        [ObservableProperty]
        private int _activeAlarmCount;

        [ObservableProperty]
        private string _currentUserName = "未登录";

        [ObservableProperty]
        private string _currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        [ObservableProperty]
        private int _axisCount;

        [ObservableProperty]
        private int _cameraCount;

        [ObservableProperty]
        private string _demoStatus = "就绪";

        [ObservableProperty]
        private string _calibrationInfo = "未标定";

        /// <summary>日志条目集合 — 绑定到UI日志面板</summary>
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        /// <summary>轴状态集合 — 绑定到运动控制面板</summary>
        public ObservableCollection<AxisStatus> AxisStatuses { get; } = new();

        public MainViewModel(
            IEventBus eventBus,
            IAlarmService alarmService,
            ILogService logService,
            IUserService userService,
            DeviceStateMachine stateMachine,
            AxisManager axisManager,
            CameraManager cameraManager,
            IndustrialTaskScheduler taskScheduler,
            VisionMotionCoordinator coordinator,
            IVisionEngine visionEngine,
            MotionViewModel motionVm,
            VisionViewModel visionVm,
            SecsGemViewModel secsGemVm,
            CommunicationViewModel commVm,
            AlarmViewModel alarmVm,
            RecipeViewModel recipeVm,
            LogViewModel logVm,
            SettingsViewModel settingsVm,
            UserViewModel userVm)
        {
            _eventBus = eventBus;
            _alarmService = alarmService;
            _logService = logService;
            _userService = userService;
            _stateMachine = stateMachine;
            _axisManager = axisManager;
            _cameraManager = cameraManager;
            _taskScheduler = taskScheduler;
            _coordinator = coordinator;
            _visionEngine = visionEngine;

            MotionVm = motionVm;
            VisionVm = visionVm;
            SecsGemVm = secsGemVm;
            CommVm = commVm;
            AlarmVm = alarmVm;
            RecipeVm = recipeVm;
            LogVm = logVm;
            SettingsVm = settingsVm;
            UserVm = userVm;

            AxisCount = axisManager.AxisCount;
            CameraCount = cameraManager.CameraCount;

            // 订阅事件
            SubscribeEvents();

            // 启动定时刷新
            StartPeriodicRefresh();
        }

        /// <summary>
        /// 订阅系统事件。
        /// </summary>
        private void SubscribeEvents()
        {
            // 设备状态变更
            _stateMachine.StateChanged += (_, args) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    DeviceStateText = GetStateDisplayText(args.To);
                    DeviceStateColor = GetStateColor(args.To);
                });
            };

            // 报警变更
            _alarmService.AlarmTriggered += (_, _) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ActiveAlarmCount = _alarmService.ActiveAlarms.Count;
                });
            };

            _alarmService.AlarmCleared += (_, _) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ActiveAlarmCount = _alarmService.ActiveAlarms.Count;
                });
            };

            // 日志新增
            _logService.LogAdded += (_, entry) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    LogEntries.Insert(0, entry);
                    if (LogEntries.Count > 500)
                        LogEntries.RemoveAt(LogEntries.Count - 1);
                });
            };

            // 用户变更
            _userService.UserChanged += (_, user) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentUserName = user?.DisplayName ?? "未登录";
                });
            };
        }

        /// <summary>
        /// 启动定时刷新（时钟和轴状态）。
        /// </summary>
        private void StartPeriodicRefresh()
        {
            _taskScheduler.RegisterTask("UI时钟刷新", async ct =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                });
                await Task.CompletedTask;
            }, 1000, TaskPriority.Background);

            _taskScheduler.RegisterTask("轴状态刷新", async ct =>
            {
                var statuses = _axisManager.GetAllStatus();
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    AxisStatuses.Clear();
                    foreach (var s in statuses) AxisStatuses.Add(s);
                });
                await Task.CompletedTask;
            }, 200, TaskPriority.Normal);

            _taskScheduler.StartAll();
        }

        // ---- 导航命令 ----

        [RelayCommand]
        private void Navigate(string page)
        {
            if (Enum.TryParse<NavigationPage>(page, out var navPage))
            {
                CurrentPage = navPage;
            }
        }

        // ---- 设备控制命令 ----

        [RelayCommand]
        private async Task InitializeDevice()
        {
            await _stateMachine.FireAsync("Initialize");
            await _axisManager.InitializeAsync();
            await _axisManager.ServoOnAllAsync();

            _logService.Log(Domain.Enums.LogLevel.Info, "系统", "设备初始化完成，所有轴已使能");
        }

        [RelayCommand]
        private async Task StartAuto()
        {
            await _stateMachine.FireAsync("StartAuto");
        }

        [RelayCommand]
        private async Task StopDevice()
        {
            await _stateMachine.FireAsync("Stop");
        }

        [RelayCommand]
        private async Task EmergencyStop()
        {
            await _axisManager.EmergencyStopAllAsync();
            await _stateMachine.FireAsync("EmergencyStop");
            _logService.Log(Domain.Enums.LogLevel.Fatal, "系统", "紧急停止！所有轴已停止运动");
        }

        [RelayCommand]
        private void ResetAlarms()
        {
            _alarmService.ClearAllAlarms(_userService.CurrentUser?.Username ?? "System");
            _ = _stateMachine.FireAsync("Reset");
        }

        [RelayCommand]
        private async Task Login()
        {
            // 简化实现：默认登录管理员
            await _userService.LoginAsync("admin", "admin123");
        }

        // ============== 新增：视觉-运动协同演示 ==============

        /// <summary>9点标定演示 — 自动执行标定流程</summary>
        [RelayCommand]
        private async Task RunCalibrationDemo()
        {
            DemoStatus = "标定中...";
            _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", "开始9点标定演示");

            await _axisManager.InitializeAsync();
            await _axisManager.ServoOnAllAsync();
            await _cameraManager.OpenAllAsync();

            var result = await _coordinator.NinePointCalibrationAsync(
                cameraId: 3, axisXId: 6, axisYId: 7,
                centerX: 0, centerY: 0, stepSize: 10);

            if (result != null)
            {
                CalibrationInfo = $"标定完成 | RMS={result.RmsError:F4}mm | 点数={result.Points.Count}";
                DemoStatus = "标定完成";
                _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", CalibrationInfo);
            }
            else
            {
                CalibrationInfo = "标定失败";
                DemoStatus = "标定失败";
            }
        }

        /// <summary>视觉对位演示 — 拍照→检测→坐标转换→运动补偿</summary>
        [RelayCommand]
        private async Task RunAlignDemo()
        {
            DemoStatus = "视觉对位中...";
            _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", "开始视觉对位演示");

            await _cameraManager.OpenAllAsync();
            var (success, errX, errY) = await _coordinator.AlignAsync(
                cameraId: 3, axisXId: 6, axisYId: 7,
                targetWorldX: 5.0, targetWorldY: 5.0);

            DemoStatus = success
                ? $"对位完成 | 补偿X={errX:F3}mm Y={errY:F3}mm"
                : "对位失败";
            _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", DemoStatus);
        }

        /// <summary>
        /// 生产流程模拟 — 完整半导体工艺流程演示
        /// 流程：上料→定位→视觉检测→搬运→检测→下料→复位
        /// </summary>
        [RelayCommand]
        private async Task RunProductionDemo()
        {
            DemoStatus = "生产模拟启动...";
            _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", "=== 生产流程模拟开始 ===");

            await _axisManager.InitializeAsync();
            await _axisManager.ServoOnAllAsync();
            await _cameraManager.OpenAllAsync();

            DemoStatus = "步骤1/7: 上料位移动";
            var axisX = _axisManager.GetAxis(0);
            var axisY = _axisManager.GetAxis(1);
            if (axisX != null) await axisX.MoveAbsoluteAsync(100, 80, 500, 500);
            if (axisY != null) await axisY.MoveAbsoluteAsync(50, 80, 500, 500);
            _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", "上料: 搬运机器人到达上料位");

            DemoStatus = "步骤2/7: 搬运到检测位";
            var axisZ = _axisManager.GetAxis(2);
            if (axisZ != null) await axisZ.MoveAbsoluteAsync(-30, 50, 300, 300);
            if (axisZ != null) await axisZ.MoveAbsoluteAsync(0, 50, 300, 300);
            if (axisX != null) await axisX.MoveAbsoluteAsync(200, 80, 500, 500);
            _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", "搬运: 产品已放置到检测位");

            DemoStatus = "步骤3/7: 视觉定位";
            var camera = _cameraManager.GetCamera(3);
            if (camera != null)
            {
                var image = await camera.CaptureAsync();
                if (image != null)
                {
                    var markResult = await _visionEngine.FindMarkAsync(image, camera.Config.ImageWidth, camera.Config.ImageHeight);
                    _logService.Log(Domain.Enums.LogLevel.Info, "协同演示",
                        $"定位: Mark点=({markResult.PixelX:F1},{markResult.PixelY:F1}) Score={markResult.Score:F2}");
                }
            }

            DemoStatus = "步骤4/7: 对位补偿";
            var alignAxis = _axisManager.GetAxis(6);
            if (alignAxis != null) await alignAxis.MoveRelativeAsync(0.5, 10, 200, 200);
            _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", "对位: 补偿运动完成");

            DemoStatus = "步骤5/7: 外观检测";
            var topCam = _cameraManager.GetCamera(0);
            if (topCam != null)
            {
                var img = await topCam.CaptureAsync();
                if (img != null)
                {
                    var defectResult = await _visionEngine.DefectDetectAsync(img, topCam.Config.ImageWidth, topCam.Config.ImageHeight, new object());
                    var hasDefect = defectResult.ExtraData.TryGetValue("HasDefect", out var d) && (bool)d;
                    _logService.Log(Domain.Enums.LogLevel.Info, "协同演示",
                        $"检测: {(hasDefect ? "发现缺陷!" : "外观OK")} Score={defectResult.Score:F2}");
                }
            }

            DemoStatus = "步骤6/7: 搬运到下料位";
            if (axisX != null) await axisX.MoveAbsoluteAsync(300, 80, 500, 500);
            if (axisZ != null) await axisZ.MoveAbsoluteAsync(-30, 50, 300, 300);
            if (axisZ != null) await axisZ.MoveAbsoluteAsync(0, 50, 300, 300);
            _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", "下料: 产品已放置到下料位");

            DemoStatus = "步骤7/7: 复位";
            if (axisX != null) await axisX.MoveAbsoluteAsync(0, 100, 500, 500);
            if (axisY != null) await axisY.MoveAbsoluteAsync(0, 100, 500, 500);
            _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", "复位: 搬运机器人回到待机位");

            DemoStatus = "生产模拟完成";
            _logService.Log(Domain.Enums.LogLevel.Info, "协同演示", "=== 生产流程模拟完成 ===");
        }

        // ---- 辅助方法 ----

        private static string GetStateDisplayText(DeviceState state) => state switch
        {
            DeviceState.Idle => "空闲",
            DeviceState.Init => "初始化中",
            DeviceState.Auto => "自动运行",
            DeviceState.Manual => "手动模式",
            DeviceState.Paused => "已暂停",
            DeviceState.Alarm => "报警",
            DeviceState.EmergencyStop => "急停",
            DeviceState.Maintenance => "维护模式",
            _ => "未知"
        };

        private static string GetStateColor(DeviceState state) => state switch
        {
            DeviceState.Idle => "#4CAF50",        // 绿色
            DeviceState.Init => "#2196F3",         // 蓝色
            DeviceState.Auto => "#00BCD4",         // 青色
            DeviceState.Manual => "#FF9800",       // 橙色
            DeviceState.Paused => "#FFC107",       // 黄色
            DeviceState.Alarm => "#F44336",        // 红色
            DeviceState.EmergencyStop => "#D32F2F",// 深红
            DeviceState.Maintenance => "#9C27B0",  // 紫色
            _ => "#757575"
        };
    }
}
