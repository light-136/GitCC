// ============================================================
// 文件：App.xaml.cs
// 用途：WPF应用启动入口 + 依赖注入容器配置
// 设计思路：
//   应用启动时配置DI容器，注册所有服务。
//   使用 Microsoft.Extensions.DependencyInjection 作为DI容器。
//   所有ViewModel通过DI容器获取依赖，而非手动new。
//   Serilog配置同时输出到文件和控制台。
// ============================================================

using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Infrastructure.DependencyInjection;
using SmartSemiCon.Application.Alarm;
using SmartSemiCon.Application.Recipe;
using SmartSemiCon.Application.StateMachine;
using SmartSemiCon.Application.TaskScheduler;
using SmartSemiCon.Application.User;
using SmartSemiCon.Hardware.Motion.Axis;
using SmartSemiCon.Hardware.Motion.Scheduler;
using SmartSemiCon.Hardware.Vision;
using SmartSemiCon.Hardware.VisionMotion;
using SmartSemiCon.UI.ViewModels;

namespace SmartSemiCon.UI
{
    /// <summary>
    /// 应用入口 — 配置DI容器并启动主窗口。
    /// </summary>
    public partial class App : System.Windows.Application
    {
        /// <summary>DI服务提供者 — 全局访问点</summary>
        public static IServiceProvider Services { get; private set; } = null!;

        /// <summary>
        /// 应用启动事件 — 配置DI容器、初始化服务、显示主窗口。
        /// </summary>
        private void App_OnStartup(object sender, StartupEventArgs e)
        {
            // 配置 Serilog 日志
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: "Logs/SmartSemiCon-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Console()
                .CreateLogger();

            // 配置DI容器
            var services = new ServiceCollection();

            // 注册日志框架
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(dispose: true);
            });

            // 注册基础设施层服务（事件总线、日志服务、SECS/GEM）
            services.AddInfrastructure();

            // 注册报警服务
            services.AddSingleton<IAlarmService, AlarmManager>();

            // 注册用户服务
            services.AddSingleton<IUserService, UserManager>();

            // 注册配方服务
            services.AddSingleton<IRecipeService>(sp =>
                new RecipeManager(
                    sp.GetRequiredService<IEventBus>(),
                    sp.GetRequiredService<ILogService>()));

            // 注册运动控制
            services.AddSingleton<AxisManager>();
            services.AddSingleton<MotionScheduler>();

            // 注册视觉系统
            services.AddSingleton<CameraManager>();
            services.AddSingleton<IVisionEngine, SimulationVisionEngine>();
            services.AddSingleton<VisionMotionCoordinator>();

            // 注册状态机
            services.AddSingleton<DeviceStateMachine>();

            // 注册任务调度器
            services.AddSingleton<IndustrialTaskScheduler>();

            // 注册ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<MotionViewModel>();
            services.AddTransient<VisionViewModel>();
            services.AddTransient<SecsGemViewModel>();
            services.AddTransient<CommunicationViewModel>();
            services.AddTransient<AlarmViewModel>();
            services.AddTransient<RecipeViewModel>();
            services.AddTransient<LogViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<UserViewModel>();

            // 注册主窗口
            services.AddTransient<MainWindow>();

            // 构建DI容器
            Services = services.BuildServiceProvider();

            // 初始化运动控制和视觉系统
            InitializeHardware();

            // 显示主窗口
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = Services.GetRequiredService<MainViewModel>();
            mainWindow.Show();
        }

        /// <summary>
        /// 初始化硬件模块 — 配置模拟轴和相机。
        /// </summary>
        private void InitializeHardware()
        {
            // 配置30轴运动控制
            var axisManager = Services.GetRequiredService<AxisManager>();
            axisManager.Configure(AxisManager.CreateDefaultConfigs());

            // 配置相机
            var cameraManager = Services.GetRequiredService<CameraManager>();
            foreach (var config in CameraManager.CreateDefaultConfigs())
            {
                cameraManager.AddCamera(config);
            }

            // 配置视觉-运动协同的标定参数
            var coordinator = Services.GetRequiredService<VisionMotionCoordinator>();
            coordinator.Transformer.SetSimpleCalibration(0.01); // 默认标定：1像素 = 0.01mm

            // 记录启动日志
            var logService = Services.GetRequiredService<ILogService>();
            logService.Log(Domain.Enums.LogLevel.Info, "系统", "SmartSemiCon 半导体设备控制平台已启动");
            logService.Log(Domain.Enums.LogLevel.Info, "系统", $"已配置 {axisManager.AxisCount} 个运动轴");
            logService.Log(Domain.Enums.LogLevel.Info, "系统", $"已配置 {cameraManager.CameraCount} 台相机");
        }

        /// <summary>
        /// 应用退出时清理资源。
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            var scheduler = Services.GetService<IndustrialTaskScheduler>();
            scheduler?.StopAll();

            var axisManager = Services.GetService<AxisManager>();
            axisManager?.Dispose();

            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
