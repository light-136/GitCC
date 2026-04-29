using System.Windows;
using System.Windows.Input;
using SmartMES.Services;
using SmartMES.Services.Alarm;
using SmartMES.Services.Automation;
using SmartMES.Services.Communication;
using SmartMES.Services.Device;
using SmartMES.Services.EventBus;
using SmartMES.Services.Logging;
using SmartMES.UI.Services;
using SmartMES.UI.ViewModels;
using SmartMES.UI.Modules.MesCommModule;
using SmartMES.UI.Modules.FileModule;
using SmartMES.UI.Modules.DatabaseModule;
using SmartMES.UI.Modules.MotionModule;
using SmartMES.UI.Modules.NativeModule;
using SmartMES.UI.Modules.Motion10AxisModule;
using SmartMES.UI.Modules.VisionModule;
using SmartMES.UI.Modules.VisionMotionModule;

namespace SmartMES.UI
{
    public partial class App : Application
    {
        /// <summary>全局快捷键钩子（应用生命周期内唯一实例）</summary>
        private GlobalKeyboardHook? _keyboardHook;

        /// <summary>
        /// 应用启动入口：初始化核心服务、构建ViewModel、注册全局热键并展示主窗口。
        /// </summary>
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ★ 全局异常捕获 — 防止未处理异常导致闪退
            SetupGlobalExceptionHandlers();

            var logger          = new LoggingService("Logs");
            var eventBus        = new EventBusService();
            var settingsService = new SettingsService("settings.json");

            try
            {
                await settingsService.LoadAsync();
                logger.LogInfo("配置加载完成", "Startup");
            }
            catch (Exception ex)
            {
                // 配置加载失败时使用默认值，但必须记录日志便于排查
                logger.LogWarning($"配置加载失败，使用默认值：{ex.Message}", "Startup");
            }

            var alarmService  = new AlarmService(logger);
            var tcpService    = new TcpCommunicationService(
                settingsService.Settings.TcpServerIp,
                settingsService.Settings.TcpServerPort, logger);
            var serialService = new SerialCommunicationService(
                settingsService.Settings.SerialPortName,
                settingsService.Settings.SerialBaudRate, logger);

            var plcForAuto = new PlcDevice("自动化PLC");
            var engine     = new AutomationEngine(logger);
            engine.AddStep(new ConnectDeviceStep(plcForAuto));
            engine.AddStep(new CollectDataStep(plcForAuto));
            engine.AddStep(new ConditionCheckStep(
                settingsService.Settings.TemperatureAlarmThreshold));
            engine.AddStep(new ExecuteActionStep(alarmService, logger));

            // 原有 ViewModel
            var dashboardVM  = new DashboardViewModel(logger, alarmService, settingsService);
            var deviceVM     = new DeviceViewModel(logger, eventBus);
            var alarmVM      = new AlarmViewModel(alarmService, logger);
            var logVM        = new LogViewModel(logger);
            var settingsVM   = new SettingsViewModel(settingsService, logger);
            var userVM       = new UserViewModel(eventBus, logger);
            var commVM       = new CommunicationViewModel(tcpService, serialService, logger);
            var automationVM = new AutomationViewModel(engine, logger);

            // 五大扩展模块
            var mesCommVM  = new MesCommViewModel();
            var fileVM     = new FileViewModel();
            var databaseVM = new DatabaseViewModel();
            var motionVM   = new MotionViewModel();
            var nativeVM   = new NativeViewModel();

            var mainVM = new MainViewModel(
                eventBus, logger,
                dashboardVM, deviceVM, alarmVM,
                logVM, settingsVM, userVM,
                commVM, automationVM,
                mesCommVM, fileVM, databaseVM, motionVM, nativeVM);

            _keyboardHook = new GlobalKeyboardHook(logger);
            _keyboardHook.RegisterHotkey(Key.F5, ModifierKeys.Control, "启动流程",
                () => Current.Dispatcher.Invoke(async () => await engine.RunAsync()));
            _keyboardHook.RegisterHotkey(Key.D, ModifierKeys.Control | ModifierKeys.Alt, "仪表盘",
                () => Current.Dispatcher.Invoke(
                    () => mainVM.NavDashboardCommand.Execute(null)));
            _keyboardHook.Install();

            var mainWindow = new Views.MainWindow { DataContext = mainVM };
            MainWindow = mainWindow;
            mainWindow.Show();
            logger.LogInfo("应用程序启动完成（含五大扩展模块+三大样例）", "Startup");
        }

        /// <summary>应用退出入口：释放全局键盘钩子等托管资源。</summary>
        protected override void OnExit(ExitEventArgs e)
        {
            _keyboardHook?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// 全局异常捕获 — 防止未处理异常导致程序闪退
        /// 捕获三类异常：UI线程、Task异步、跨线程
        /// </summary>
        private void SetupGlobalExceptionHandlers()
        {
            // 1. UI线程未处理异常
            DispatcherUnhandledException += (_, e) =>
            {
                ShowError("UI线程异常", e.Exception);
                e.Handled = true;
            };

            // 2. 非UI线程（Task/后台线程）未处理异常
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ShowError("后台任务异常", e.Exception);
                e.SetObserved(); // 阻止进程终止
            };

            // 3. AppDomain 级别兜底
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                var msg = ex?.Message ?? e.ExceptionObject?.ToString() ?? "未知错误";
                // AppDomain异常无法阻止退出，但至少记录并提示
                try
                {
                    MessageBox.Show(
                        $"严重错误（程序可能需要重启）:\n{msg}",
                        "SmartMES — 严重异常",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { /* 连MessageBox都失败时静默 */ }
            };
        }

        /// <summary>统一异常展示：弹窗提示，不闪退</summary>
        private static void ShowError(string source, Exception ex)
        {
            try
            {
                // 过滤已知无害的XAML绑定警告
                if (ex is System.Windows.Markup.XamlParseException
                    || ex.GetType().Name.Contains("Binding"))
                {
                    // 仅记录，不弹窗
                    System.Diagnostics.Debug.WriteLine($"[{source}] {ex.Message}");
                    return;
                }

                var msg = $"{source}\n\n{ex.GetType().Name}:\n{ex.Message}";
                if (ex.InnerException != null)
                    msg += $"\n\n内部异常:\n{ex.InnerException.Message}";

                Current?.Dispatcher.Invoke(() =>
                    MessageBox.Show(msg, $"SmartMES — {source}",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
            }
            catch { /* 异常处理本身不能抛异常 */ }
        }
    }
}
