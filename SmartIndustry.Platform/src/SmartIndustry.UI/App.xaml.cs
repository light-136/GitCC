// 文件：App.xaml.cs
// 层级：表现层（UI）
// 职责：WPF 应用程序入口，初始化依赖注入容器和全局异常处理

using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SmartIndustry.Infrastructure.DependencyInjection;
using SmartIndustry.Hardware.DependencyInjection;
using AppDI = SmartIndustry.Application.DependencyInjection;

namespace SmartIndustry.UI
{
    /// <summary>
    /// WPF 应用程序入口类
    /// </summary>
    public partial class App : System.Windows.Application
    {
        /// <summary>依赖注入服务提供者</summary>
        public static IServiceProvider? Services { get; private set; }

        /// <summary>
        /// 应用启动时初始化 DI 容器
        /// </summary>
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();

            // 初始化数据库
            await Services.EnsureDatabaseCreatedAsync();
        }

        /// <summary>
        /// 注册服务到 DI 容器
        /// </summary>
        private void ConfigureServices(IServiceCollection services)
        {
            // 基础设施层：数据库、事件总线、日志、文件、网络
            services.AddInfrastructure("Data Source=smartindustry.db");

            // 硬件抽象层：运动控制、视觉、IO（默认仿真模式）
            services.AddHardware();

            // 应用层：状态机、报警、配方、用户、调度、自动化
            AppDI.ServiceCollectionExtensions.AddApplication(services);
        }
    }
}
