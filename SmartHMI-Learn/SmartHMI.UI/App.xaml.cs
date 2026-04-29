using Microsoft.Extensions.DependencyInjection;
using SmartHMI.Core.EventBus;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.IO;
using SmartHMI.Modules.Ai;
using SmartHMI.Modules.Cloud;
using SmartHMI.Modules.Communication;
using SmartHMI.Modules.Device;
using SmartHMI.Modules.Mes;
using SmartHMI.Modules.Motion;
using SmartHMI.Modules.Recipe;
using SmartHMI.Modules.Report;
using SmartHMI.Modules.Safety;
using SmartHMI.Modules.SecsGem;
using SmartHMI.Modules.Traceability;
using SmartHMI.Modules.Vision;
using SmartHMI.Services;
using SmartHMI.UI.Services;
using SmartHMI.UI.ViewModels;
using System.Windows;

namespace SmartHMI.UI;

/// <summary>
/// App.xaml.cs — 应用程序启动入口
/// 职责：
///   1. 配置 DI 容器，注册所有服务、模块、ViewModel
///   2. 在 OnStartup 中手动创建 MainWindow（通过 DI 解析，支持构造函数注入）
///   3. 提供静态 Services 属性，供全局访问 DI 容器（仅在必要时使用）
/// 注意：
///   App.xaml 中已移除 StartupUri，改为此处手动控制窗口创建，
///   这样 MainWindow 可以通过构造函数接收 ViewModel（标准 MVVM + DI 模式）。
/// </summary>
public partial class App : Application
{
    /// <summary>全局 DI 服务容器（只读，构建后不可修改）</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// 应用程序启动事件处理
    /// 执行顺序：配置 DI → 构建容器 → 创建主窗口 → 显示
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. 创建服务集合并注册所有依赖
        var services = new ServiceCollection();
        ConfigureServices(services);

        // 2. 构建 DI 容器
        Services = services.BuildServiceProvider();

        // 3. 通过 DI 解析 MainWindow（自动注入 MainViewModel）
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <summary>
    /// 配置 DI 服务注册
    /// 注册顺序：基础设施 → 服务层 → 模块层 → 导航服务 → ViewModel → 窗口
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // ===== 基础设施层 =====
        // EventAggregator：事件总线，模块间解耦通信
        services.AddSingleton<EventAggregator>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<EventAggregator>());

        // SimulatedIoDevice：模拟 IO 设备（无需真实硬件）
        services.AddSingleton<IIoDevice, SimulatedIoDevice>();

        // ===== 服务层 =====
        services.AddSingleton<IAlarmService, AlarmService>();
        // LoggingService 同时注册为具体类型和接口（LogViewModel 需要具体类型访问 EntryAdded 事件）
        services.AddSingleton<LoggingService>();
        services.AddSingleton<ILoggingService>(sp => sp.GetRequiredService<LoggingService>());
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IUserService, UserService>();

        // ===== 模块层 =====
        services.AddSingleton<CommunicationManager>();
        services.AddSingleton<ICommunicationService>(sp => sp.GetRequiredService<CommunicationManager>());
        services.AddSingleton<DeviceManager>();
        services.AddSingleton<MotionManager>();
        // 第3-5阶段模块
        services.AddSingleton<ISafetyInterlockService, SafetyInterlockService>();
        services.AddSingleton<IVisionService, VisionService>();
        services.AddSingleton<VisionMotionCoordinator>();
        services.AddSingleton<IRecipeService, RecipeService>();
        services.AddSingleton<ITraceabilityService, TraceabilityService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IMesConnector, MesConnector>();
        // 第6阶段模块
        services.AddSingleton<ICloudSyncService, CloudSyncService>();
        services.AddSingleton<ISecsGemService, SecsGemService>();
        services.AddSingleton<IAiAssistantService, AiAssistantService>();
        services.AddSingleton<VisionAnalysisService>();

        // ===== 导航服务 =====
        // NavigationService 需要 IServiceProvider，使用工厂方法注册
        services.AddSingleton<INavigationService>(sp => new NavigationService(sp));

        // ===== ViewModel 层 =====
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DeviceViewModel>();
        services.AddSingleton<AlarmViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<CommunicationViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<UserViewModel>();
        services.AddSingleton<MotionViewModel>();
        // 第3-5阶段 ViewModel
        services.AddSingleton<SafetyViewModel>();
        services.AddSingleton<VisionViewModel>();
        services.AddSingleton<RecipeViewModel>();
        services.AddSingleton<TraceabilityViewModel>();
        services.AddSingleton<ReportViewModel>();
        services.AddSingleton<MesViewModel>();
        // 第6阶段 ViewModel
        services.AddSingleton<CloudSyncViewModel>();
        services.AddSingleton<SecsGemViewModel>();
        services.AddSingleton<AiAssistantViewModel>();

        // ===== 窗口 =====
        services.AddSingleton<MainWindow>();
    }
}
