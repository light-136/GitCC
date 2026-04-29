using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;
using SmartMES.Services.Alarm;
using SmartMES.Services.Automation;
using SmartMES.Services.Communication;
using SmartMES.Services.EventBus;
using SmartMES.Services.Logging;
using SmartMES.Services;
using SmartMES.UI.Modules.DatabaseModule;
using SmartMES.UI.Modules.FileModule;
using SmartMES.UI.Modules.MesCommModule;
using SmartMES.UI.Modules.MotionModule;
using SmartMES.UI.Modules.NativeModule;
using SmartMES.UI.ViewModels;

namespace SmartMES.Tests;

public class MainViewModelPermissionTests
{
    [Fact]
    public void LoginEvent_ShouldApplyMenuPermissions_ForViewer()
    {
        var eventBus = new EventBusService();
        var vm = CreateMainViewModel(eventBus);

        eventBus.Publish(new UserLoginEvent
        {
            Username = "观察员",
            IsLogin = true,
            Role = "观察员",
            Permissions = PagePermissions.ForRole(UserRole.Viewer)
        });

        Assert.Equal("观察员", vm.CurrentUser);
        Assert.False(vm.CanAccessCommunication);
        Assert.False(vm.CanAccessMotion);
        Assert.False(vm.CanAccessSettings);
        Assert.False(vm.CanAccessUser);
        Assert.False(vm.CanAccessIndustrial);
        Assert.True(vm.CanAccessDashboard);
        Assert.True(vm.CanAccessVision);
    }

    [Fact]
    public void LoginEvent_ShouldFallbackToFirstAllowedPage_WhenCurrentPageBecomesForbidden()
    {
        var eventBus = new EventBusService();
        var vm = CreateMainViewModel(eventBus);

        vm.NavMotionCommand.Execute(null);
        Assert.Same(vm.MotionVM, vm.CurrentPage);

        eventBus.Publish(new UserLoginEvent
        {
            Username = "观察员",
            IsLogin = true,
            Role = "观察员",
            Permissions = PagePermissions.ForRole(UserRole.Viewer)
        });

        Assert.Same(vm.DashboardVM, vm.CurrentPage);
        Assert.Equal("实时仪表盘", vm.CurrentPageTitle);
    }

    [Fact]
    public void LogoutEvent_ShouldRestoreDefaultFullAccessMode()
    {
        var eventBus = new EventBusService();
        var vm = CreateMainViewModel(eventBus);

        eventBus.Publish(new UserLoginEvent
        {
            Username = "观察员",
            IsLogin = true,
            Role = "观察员",
            Permissions = PagePermissions.ForRole(UserRole.Viewer)
        });

        Assert.False(vm.CanAccessMotion);

        eventBus.Publish(new UserLoginEvent
        {
            Username = "观察员",
            IsLogin = false,
            Role = string.Empty,
            Permissions = null
        });

        Assert.Equal("未登录", vm.CurrentUser);
        Assert.True(vm.CanAccessMotion);
        Assert.True(vm.CanAccessSettings);
        Assert.True(vm.CanAccessUser);
        Assert.True(vm.CanAccessIndustrial);
    }

    private static MainViewModel CreateMainViewModel(IEventBus eventBus)
    {
        ILoggingService logger = new LoggingService(Path.Combine(Path.GetTempPath(), "SmartMES.Tests", Guid.NewGuid().ToString("N")));
        IAlarmService alarmService = new AlarmService(logger);
        ISettingsService settingsService = new SettingsService(Path.Combine(Path.GetTempPath(), "SmartMES.Tests", $"settings-{Guid.NewGuid():N}.json"));

        var dashboard = new DashboardViewModel(logger, alarmService, settingsService);
        var device = new DeviceViewModel(logger, eventBus);
        var alarm = new AlarmViewModel(alarmService, logger);
        var log = new LogViewModel(logger);
        var settings = new SettingsViewModel(settingsService, logger);
        var user = new UserViewModel(eventBus, logger);
        var tcp = new TcpCommunicationService("127.0.0.1", 502, logger);
        var serial = new SerialCommunicationService("COM1", 9600, logger);
        var communication = new CommunicationViewModel(tcp, serial, logger);

        var engine = new AutomationEngine(logger);
        engine.AddStep(new ConditionCheckStep(50));
        var automation = new AutomationViewModel(engine, logger);

        var mes = new MesCommViewModel();
        var file = new FileViewModel();
        var database = new DatabaseViewModel();
        var motion = new MotionViewModel();
        var native = new NativeViewModel();

        return new MainViewModel(
            eventBus,
            logger,
            dashboard,
            device,
            alarm,
            log,
            settings,
            user,
            communication,
            automation,
            mes,
            file,
            database,
            motion,
            native);
    }
}
