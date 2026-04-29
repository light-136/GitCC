using SmartMES.Core.Infrastructure;
using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;
using SmartMES.UI.Modules.DatabaseModule;
using SmartMES.UI.Modules.FileModule;
using SmartMES.UI.Modules.IndustrialModule;
using SmartMES.UI.Modules.MesCommModule;
using SmartMES.UI.Modules.Motion10AxisModule;
using SmartMES.UI.Modules.MotionModule;
using SmartMES.UI.Modules.NativeModule;
using SmartMES.UI.Modules.VisionModule;
using SmartMES.UI.Modules.ReportModule;
using SmartMES.UI.Modules.VisionMotionModule;
using System.Windows;
using System.Windows.Threading;

namespace SmartMES.UI.ViewModels
{
    /// <summary>
    /// 主窗口导航与全局状态协调器。
    /// 设计职责：
    /// 1) 统一管理所有页面ViewModel实例生命周期；
    /// 2) 提供左侧菜单导航命令并维护当前标题；
    /// 3) 订阅用户登录事件，维护顶部用户信息；
    /// 4) 提供系统时钟刷新。
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly IEventBus _eventBus;
        private readonly ILoggingService _logger;

        private ViewModelBase? _currentPage;
        private string _currentPageTitle = "仪表盘";
        private string _systemTime = string.Empty;
        private string _currentUser = "未登录";
        private string _currentUserRole = string.Empty;
        private PagePermissions? _currentPermissions;

        public DashboardViewModel DashboardVM { get; }
        public DeviceViewModel DeviceVM { get; }
        public AlarmViewModel AlarmVM { get; }
        public LogViewModel LogVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public UserViewModel UserVM { get; }
        public CommunicationViewModel CommunicationVM { get; }
        public AutomationViewModel AutomationVM { get; }

        public MesCommViewModel MesCommVM { get; }
        public FileViewModel FileVM { get; }
        public DatabaseViewModel DatabaseVM { get; }
        public MotionViewModel MotionVM { get; }
        public NativeViewModel NativeVM { get; }

        public Motion10AxisViewModel Motion10VM { get; }
        public VisionViewModel VisionVM { get; }
        public VisionMotionViewModel VisionMotionVM { get; }

        public IndustrialViewModel IndustrialVM { get; }

        /// <summary>报表统计模块 ViewModel</summary>
        public ReportViewModel ReportVM { get; }

        public ViewModelBase? CurrentPage
        {
            get => _currentPage;
            set => SetProperty(ref _currentPage, value);
        }

        public string CurrentPageTitle
        {
            get => _currentPageTitle;
            set => SetProperty(ref _currentPageTitle, value);
        }

        public string SystemTime
        {
            get => _systemTime;
            set => SetProperty(ref _systemTime, value);
        }

        public string CurrentUser
        {
            get => _currentUser;
            set => SetProperty(ref _currentUser, value);
        }

        public string CurrentUserRole
        {
            get => _currentUserRole;
            set => SetProperty(ref _currentUserRole, value);
        }

        public bool CanAccessDashboard => HasPermission(p => p.Dashboard);
        public bool CanAccessDevice => HasPermission(p => p.Device);
        public bool CanAccessAlarm => HasPermission(p => p.Alarm);
        public bool CanAccessLog => HasPermission(p => p.Log);
        public bool CanAccessSettings => HasPermission(p => p.Settings);
        public bool CanAccessUser => HasPermission(p => p.UserManage);
        public bool CanAccessCommunication => HasPermission(p => p.Communication);
        public bool CanAccessAutomation => HasPermission(p => p.Automation);
        public bool CanAccessMesComm => HasPermission(p => p.MesComm);
        public bool CanAccessFile => HasPermission(p => p.FileProcess);
        public bool CanAccessDatabase => HasPermission(p => p.Database);
        public bool CanAccessMotion => HasPermission(p => p.Motion);
        public bool CanAccessNative => HasPermission(p => p.Native);
        public bool CanAccessMotion10 => HasPermission(p => p.Motion10Axis);
        public bool CanAccessVision => HasPermission(p => p.Vision);
        public bool CanAccessVisionMotion => HasPermission(p => p.VisionMotion);
        public bool CanAccessIndustrial => HasPermission(p => p.Industrial);

        /// <summary>报表模块权限</summary>
        public bool CanAccessReport => HasPermission(p => p.Dashboard);
        public bool HasSampleAccess => CanAccessMotion10 || CanAccessVision || CanAccessVisionMotion;

        public RelayCommand NavDashboardCommand { get; }
        public RelayCommand NavDeviceCommand { get; }
        public RelayCommand NavAlarmCommand { get; }
        public RelayCommand NavLogCommand { get; }
        public RelayCommand NavSettingsCommand { get; }
        public RelayCommand NavUserCommand { get; }
        public RelayCommand NavCommunicationCommand { get; }
        public RelayCommand NavAutomationCommand { get; }
        public RelayCommand NavMesCommCommand { get; }
        public RelayCommand NavFileCommand { get; }
        public RelayCommand NavDatabaseCommand { get; }
        public RelayCommand NavMotionCommand { get; }
        public RelayCommand NavNativeCommand { get; }
        public RelayCommand NavMotion10Command { get; }
        public RelayCommand NavVisionCommand { get; }
        public RelayCommand NavVisionMotionCommand { get; }
        public RelayCommand NavIndustrialCommand { get; }

        /// <summary>导航到报表统计模块</summary>
        public RelayCommand NavReportCommand { get; }

        /// <summary>创建MainViewModel并初始化导航、命令、事件订阅与系统时钟。</summary>
        public MainViewModel(
            IEventBus eventBus, ILoggingService logger,
            DashboardViewModel dashboardVM, DeviceViewModel deviceVM,
            AlarmViewModel alarmVM, LogViewModel logVM,
            SettingsViewModel settingsVM, UserViewModel userVM,
            CommunicationViewModel communicationVM, AutomationViewModel automationVM,
            MesCommViewModel mesCommVM, FileViewModel fileVM,
            DatabaseViewModel databaseVM, MotionViewModel motionVM,
            NativeViewModel nativeVM)
        {
            _eventBus = eventBus;
            _logger = logger;

            DashboardVM = dashboardVM;
            DeviceVM = deviceVM;
            AlarmVM = alarmVM;
            LogVM = logVM;
            SettingsVM = settingsVM;
            UserVM = userVM;
            CommunicationVM = communicationVM;
            AutomationVM = automationVM;
            MesCommVM = mesCommVM;
            FileVM = fileVM;
            DatabaseVM = databaseVM;
            MotionVM = motionVM;
            NativeVM = nativeVM;

            Motion10VM = new Motion10AxisViewModel();
            VisionVM = new VisionViewModel();
            VisionMotionVM = new VisionMotionViewModel();
            IndustrialVM = new IndustrialViewModel();
            ReportVM = new ReportViewModel();

            CurrentPage = DashboardVM;

            NavDashboardCommand = new RelayCommand(_ => Navigate(DashboardVM, "实时仪表盘"));
            NavDeviceCommand = new RelayCommand(_ => Navigate(DeviceVM, "设备管理"));
            NavAlarmCommand = new RelayCommand(_ => Navigate(AlarmVM, "报警管理"));
            NavLogCommand = new RelayCommand(_ => Navigate(LogVM, "系统日志"));
            NavSettingsCommand = new RelayCommand(_ => Navigate(SettingsVM, "参数配置"));
            NavUserCommand = new RelayCommand(_ => Navigate(UserVM, "用户管理"));
            NavCommunicationCommand = new RelayCommand(_ => Navigate(CommunicationVM, "通信监控"));
            NavAutomationCommand = new RelayCommand(_ => Navigate(AutomationVM, "自动化流程"));
            NavMesCommCommand = new RelayCommand(_ => Navigate(MesCommVM, "MES通信"));
            NavFileCommand = new RelayCommand(_ => Navigate(FileVM, "文件处理"));
            NavDatabaseCommand = new RelayCommand(_ => Navigate(DatabaseVM, "数据库"));
            NavMotionCommand = new RelayCommand(_ => Navigate(MotionVM, "运动控制中心"));
            NavNativeCommand = new RelayCommand(_ => Navigate(NativeVM, "C++ Native"));
            NavMotion10Command = new RelayCommand(_ => Navigate(Motion10VM, "10轴版面切换"));
            NavVisionCommand = new RelayCommand(_ => Navigate(VisionVM, "视觉检测样例"));
            NavVisionMotionCommand = new RelayCommand(_ => Navigate(VisionMotionVM, "视觉+运动协同"));
            NavIndustrialCommand = new RelayCommand(_ => Navigate(IndustrialVM, "工业系统总览"));
            NavReportCommand     = new RelayCommand(_ => Navigate(ReportVM,     "报表统计"));

            _eventBus.Subscribe<UserLoginEvent>(ev =>
            {
                CurrentUser = ev.IsLogin ? ev.Username : "未登录";
                CurrentUserRole = ev.IsLogin ? ev.Role : string.Empty;
                _currentPermissions = ev.IsLogin ? ev.Permissions : null;
                RefreshPermissionFlags();
                EnsureCurrentPageAccessible();
            });

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, __) => SystemTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            timer.Start();

            _logger.LogInfo("主系统启动（含五大模块+三大样例+工业级能力）", "MainSystem");
        }

        /// <summary>切换当前页面并同步更新页面标题。</summary>
        private void Navigate(ViewModelBase page, string title)
        {
            CurrentPage = page;
            CurrentPageTitle = title;
        }

        private bool HasPermission(Func<PagePermissions, bool> selector)
            => _currentPermissions == null || selector(_currentPermissions);

        private void RefreshPermissionFlags()
        {
            OnPropertyChanged(nameof(CanAccessDashboard));
            OnPropertyChanged(nameof(CanAccessDevice));
            OnPropertyChanged(nameof(CanAccessAlarm));
            OnPropertyChanged(nameof(CanAccessLog));
            OnPropertyChanged(nameof(CanAccessSettings));
            OnPropertyChanged(nameof(CanAccessUser));
            OnPropertyChanged(nameof(CanAccessCommunication));
            OnPropertyChanged(nameof(CanAccessAutomation));
            OnPropertyChanged(nameof(CanAccessMesComm));
            OnPropertyChanged(nameof(CanAccessFile));
            OnPropertyChanged(nameof(CanAccessDatabase));
            OnPropertyChanged(nameof(CanAccessMotion));
            OnPropertyChanged(nameof(CanAccessNative));
            OnPropertyChanged(nameof(CanAccessMotion10));
            OnPropertyChanged(nameof(CanAccessVision));
            OnPropertyChanged(nameof(CanAccessVisionMotion));
            OnPropertyChanged(nameof(CanAccessIndustrial));
            OnPropertyChanged(nameof(CanAccessReport));
            OnPropertyChanged(nameof(HasSampleAccess));
        }

        private void EnsureCurrentPageAccessible()
        {
            if (CurrentPage == null)
            {
                Navigate(DashboardVM, "实时仪表盘");
                return;
            }

            if (CurrentPage == DashboardVM && CanAccessDashboard) return;
            if (CurrentPage == DeviceVM && CanAccessDevice) return;
            if (CurrentPage == AlarmVM && CanAccessAlarm) return;
            if (CurrentPage == LogVM && CanAccessLog) return;
            if (CurrentPage == SettingsVM && CanAccessSettings) return;
            if (CurrentPage == UserVM && CanAccessUser) return;
            if (CurrentPage == CommunicationVM && CanAccessCommunication) return;
            if (CurrentPage == AutomationVM && CanAccessAutomation) return;
            if (CurrentPage == MesCommVM && CanAccessMesComm) return;
            if (CurrentPage == FileVM && CanAccessFile) return;
            if (CurrentPage == DatabaseVM && CanAccessDatabase) return;
            if (CurrentPage == MotionVM && CanAccessMotion) return;
            if (CurrentPage == NativeVM && CanAccessNative) return;
            if (CurrentPage == Motion10VM && CanAccessMotion10) return;
            if (CurrentPage == VisionVM && CanAccessVision) return;
            if (CurrentPage == VisionMotionVM && CanAccessVisionMotion) return;
            if (CurrentPage == IndustrialVM && CanAccessIndustrial) return;
            if (CurrentPage == ReportVM && CanAccessReport) return;

            var fallbackPages = new (bool allowed, ViewModelBase page, string title)[]
            {
                (CanAccessDashboard, DashboardVM, "实时仪表盘"),
                (CanAccessDevice, DeviceVM, "设备管理"),
                (CanAccessAlarm, AlarmVM, "报警管理"),
                (CanAccessLog, LogVM, "系统日志"),
                (CanAccessCommunication, CommunicationVM, "通信监控"),
                (CanAccessVision, VisionVM, "视觉检测样例"),
                (CanAccessAutomation, AutomationVM, "自动化流程"),
                (CanAccessMesComm, MesCommVM, "MES通信"),
                (CanAccessFile, FileVM, "文件处理"),
                (CanAccessDatabase, DatabaseVM, "数据库"),
                (CanAccessMotion, MotionVM, "运动控制中心"),
                (CanAccessNative, NativeVM, "C++ Native"),
                (CanAccessMotion10, Motion10VM, "10轴版面切换"),
                (CanAccessVisionMotion, VisionMotionVM, "视觉+运动协同"),
                (CanAccessIndustrial, IndustrialVM, "工业系统总览"),
                (CanAccessSettings, SettingsVM, "参数配置"),
                (CanAccessUser, UserVM, "用户管理")
            };

            var nextPage = fallbackPages.FirstOrDefault(x => x.allowed);
            if (nextPage.page != null)
            {
                Navigate(nextPage.page, nextPage.title);
                return;
            }

            MessageBox.Show("当前账号没有可访问页面，请联系管理员分配权限。", "权限提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            Navigate(DashboardVM, "实时仪表盘");
        }
    }
}
