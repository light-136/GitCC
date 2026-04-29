using SmartHMI.Core.Events;
using SmartHMI.Core.Interfaces;
using SmartHMI.UI.Services;

namespace SmartHMI.UI.ViewModels;

/// <summary>
/// 主窗口 ViewModel — 应用程序的顶层 ViewModel
/// 职责：
///   1. 持有 CurrentViewModel，驱动 MainWindow 的 ContentControl 切换页面
///   2. 管理导航命令（侧边栏按钮点击 → 切换 CurrentViewModel）
///   3. 显示全局状态：当前用户、活动报警数、系统时间、状态栏消息
///   4. 处理登录/退出逻辑
/// MVVM 说明：
///   MainWindow.xaml 的 ContentControl 绑定 CurrentViewModel，
///   WPF 根据 App.xaml 中注册的 DataTemplate 自动渲染对应 View。
/// </summary>
public class MainViewModel : BaseViewModel
{
    // --- 依赖注入的服务 ---
    private readonly IUserService _userService;
    private readonly INavigationService _navigationService;
    private readonly IAlarmService _alarmService;

    // --- 私有字段（对应属性的后备字段）---
    private object? _currentViewModel;      // 当前显示的 ViewModel
    private string _currentPageTitle = "仪表盘";
    private string _statusMessage = "系统就绪";
    private int _activeAlarmCount;
    private string _currentUserDisplay = "未登录";
    private bool _isLoggedIn;

    // ===================== 公开属性 =====================

    /// <summary>
    /// 当前显示的 ViewModel
    /// MainWindow 的 ContentControl 绑定此属性，
    /// WPF 自动根据 DataTemplate 渲染对应 View
    /// </summary>
    public object? CurrentViewModel
    {
        get => _currentViewModel;
        set => SetField(ref _currentViewModel, value);
    }

    /// <summary>当前页面标题（显示在顶部标题栏）</summary>
    public string CurrentPageTitle
    {
        get => _currentPageTitle;
        set => SetField(ref _currentPageTitle, value);
    }

    /// <summary>状态栏消息（显示在底部状态栏）</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    /// <summary>活动报警数量（顶部报警徽章）</summary>
    public int ActiveAlarmCount
    {
        get => _activeAlarmCount;
        set => SetField(ref _activeAlarmCount, value);
    }

    /// <summary>当前登录用户显示名（顶部右侧）</summary>
    public string CurrentUserDisplay
    {
        get => _currentUserDisplay;
        set => SetField(ref _currentUserDisplay, value);
    }

    /// <summary>是否已登录（控制导航菜单可见性）</summary>
    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        set => SetField(ref _isLoggedIn, value);
    }

    /// <summary>当前系统时间（每秒刷新）</summary>
    public string CurrentTime => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    // ===================== 命令 =====================

    /// <summary>导航命令：参数为页面标识字符串</summary>
    public RelayCommand NavigateCommand { get; }

    /// <summary>退出登录命令</summary>
    public RelayCommand LogoutCommand { get; }

    // ===================== 构造函数 =====================

    /// <summary>
    /// 构造函数 — 通过 DI 注入所有依赖
    /// </summary>
    /// <param name="userService">用户服务</param>
    /// <param name="navigationService">导航服务</param>
    /// <param name="eventBus">事件总线</param>
    /// <param name="alarmService">报警服务</param>
    public MainViewModel(
        IUserService userService,
        INavigationService navigationService,
        IEventBus eventBus,
        IAlarmService alarmService)
    {
        _userService = userService;
        _navigationService = navigationService;
        _alarmService = alarmService;

        // 初始化命令
        NavigateCommand = new RelayCommand(p => Navigate(p?.ToString() ?? ""));
        LogoutCommand = new RelayCommand(Logout);

        // 订阅导航服务的 ViewModel 变化事件
        _navigationService.CurrentViewModelChanged += (_, _) =>
            CurrentViewModel = _navigationService.CurrentViewModel;

        // 订阅事件总线：用户登录/退出
        eventBus.Subscribe<UserLoginEvent>(OnUserLogin);

        // 订阅事件总线：报警变化
        eventBus.Subscribe<NewAlarmEvent>(_ =>
            ActiveAlarmCount = _alarmService.ActiveAlarms.Count);
        eventBus.Subscribe<AlarmClearedEvent>(_ =>
            ActiveAlarmCount = _alarmService.ActiveAlarms.Count);

        // 启动时钟定时器（每秒刷新 CurrentTime）
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += (_, _) => OnPropertyChanged(nameof(CurrentTime));
        timer.Start();

        // 默认显示登录页
        _navigationService.NavigateTo<LoginViewModel>();
    }

    // ===================== 私有方法 =====================

    /// <summary>
    /// 执行导航：根据页面标识切换 ViewModel
    /// </summary>
    /// <param name="page">页面标识（如 "Dashboard"、"Device"）</param>
    private void Navigate(string page)
    {
        // 更新页面标题
        CurrentPageTitle = page switch
        {
            "Dashboard"     => "仪表盘",
            "Device"        => "设备管理",
            "Communication" => "通信监控",
            "Motion"        => "运动控制",
            "Alarm"         => "报警管理",
            "Log"           => "系统日志",
            "Settings"      => "参数配置",
            "User"          => "用户管理",
            "Safety"        => "安全互锁",
            "Vision"        => "视觉检测",
            "Recipe"        => "配方管理",
            "Traceability"  => "追溯系统",
            "Report"        => "报表导出",
            "Mes"           => "MES 协同",
            "CloudSync"     => "云端同步",
            "SecsGem"       => "SECS/GEM",
            "AiAssistant"   => "AI 助手",
            _               => page
        };

        switch (page)
        {
            case "Dashboard":     _navigationService.NavigateTo<DashboardViewModel>(); break;
            case "Device":        _navigationService.NavigateTo<DeviceViewModel>(); break;
            case "Communication": _navigationService.NavigateTo<CommunicationViewModel>(); break;
            case "Motion":        _navigationService.NavigateTo<MotionViewModel>(); break;
            case "Alarm":         _navigationService.NavigateTo<AlarmViewModel>(); break;
            case "Log":           _navigationService.NavigateTo<LogViewModel>(); break;
            case "Settings":      _navigationService.NavigateTo<SettingsViewModel>(); break;
            case "User":          _navigationService.NavigateTo<UserViewModel>(); break;
            case "Safety":        _navigationService.NavigateTo<SafetyViewModel>(); break;
            case "Vision":        _navigationService.NavigateTo<VisionViewModel>(); break;
            case "Recipe":        _navigationService.NavigateTo<RecipeViewModel>(); break;
            case "Traceability":  _navigationService.NavigateTo<TraceabilityViewModel>(); break;
            case "Report":        _navigationService.NavigateTo<ReportViewModel>(); break;
            case "Mes":           _navigationService.NavigateTo<MesViewModel>(); break;
            case "CloudSync":     _navigationService.NavigateTo<CloudSyncViewModel>(); break;
            case "SecsGem":       _navigationService.NavigateTo<SecsGemViewModel>(); break;
            case "AiAssistant":   _navigationService.NavigateTo<AiAssistantViewModel>(); break;
        }

        StatusMessage = $"已切换到：{CurrentPageTitle}";
    }

    /// <summary>
    /// 处理用户登录/退出事件
    /// </summary>
    private void OnUserLogin(UserLoginEvent e)
    {
        IsLoggedIn = e.IsLogin;
        CurrentUserDisplay = e.IsLogin && e.User != null
            ? $"{e.User.DisplayName}  [{e.User.Role}]"
            : "未登录";

        if (e.IsLogin)
        {
            // 登录成功后自动导航到仪表盘
            Navigate("Dashboard");
            StatusMessage = $"欢迎，{e.User?.DisplayName}！";
        }
        else
        {
            // 退出后回到登录页
            _navigationService.NavigateTo<LoginViewModel>();
            CurrentPageTitle = "登录";
            StatusMessage = "已退出登录";
        }
    }

    /// <summary>退出登录</summary>
    private void Logout()
    {
        _userService.Logout();
    }
}
