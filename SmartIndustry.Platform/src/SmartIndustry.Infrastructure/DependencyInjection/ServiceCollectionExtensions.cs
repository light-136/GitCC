// ============================================================
// 文件：ServiceCollectionExtensions.cs
// 层次：基础设施层 (Infrastructure Layer) — DI 注册扩展
// 职责：
//   提供 AddInfrastructure 扩展方法，一次性注册所有基础设施层服务到 DI 容器。
//   调用方（SmartIndustry.UI 的 App.xaml.cs 或 WPF 启动代码）只需一行代码即可完成注册：
//     services.AddInfrastructure(connectionString)
//   注册策略说明：
//     - AppDbContext：Scoped（每个业务操作一个实例，保证事务一致性）
//     - IEventBus（EventBusService）：Singleton（整个应用生命周期共享，是事件路由中心）
//     - ILogService（SerilogService）：Singleton（全局日志，共享写入实例）
//     - IFileService（FileService）：Singleton（无状态，可安全共享）
//     - IRepository<T>（GenericRepository）：Scoped（随 DbContext 生命周期）
//     - IAlarmRepository / IUserRepository：Scoped（同上）
//     - IHttpClientService（HttpClientService）：Singleton（通过 HttpClientFactory 管理 HttpClient）
//     - JsonConfigService：Singleton（配置全局共享，热重载影响所有消费者）
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Interfaces.Repositories;
using SmartIndustry.Infrastructure.Configuration;
using SmartIndustry.Infrastructure.Database.Context;
using SmartIndustry.Infrastructure.Database.Repositories;
using SmartIndustry.Infrastructure.EventBus;
using SmartIndustry.Infrastructure.FileSystem;
using SmartIndustry.Infrastructure.Logging;
using SmartIndustry.Infrastructure.Network.Http;

namespace SmartIndustry.Infrastructure.DependencyInjection
{
    /// <summary>
    /// IServiceCollection 扩展方法集合，提供基础设施层服务的批量 DI 注册。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册所有基础设施层服务。
        /// </summary>
        /// <param name="services">DI 服务容器（由 WPF App 或 Host 提供）</param>
        /// <param name="sqliteConnectionString">SQLite 连接字符串，如 "Data Source=app.db" </param>
        /// <param name="logDirectory">日志文件目录（null=使用应用目录下的 Logs 子目录）</param>
        /// <param name="configFilePath">配置文件路径（null=使用应用目录下的 appsettings.json）</param>
        /// <param name="safeDeleteRootDirectory">
        ///   文件删除操作的安全根目录（FileService 只允许删除此目录内的文件）。
        ///   null=不限制（仅在受信任的内部场景使用）
        /// </param>
        /// <returns>返回 IServiceCollection 以支持链式调用</returns>
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            string sqliteConnectionString,
            string? logDirectory = null,
            string? configFilePath = null,
            string? safeDeleteRootDirectory = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrWhiteSpace(sqliteConnectionString))
                throw new ArgumentException("SQLite 连接字符串不能为空", nameof(sqliteConnectionString));

            // ================================================================
            // 1. 数据库层：EF Core + SQLite
            // ================================================================

            // AppDbContext 注册为 Scoped（每个业务单元使用独立实例，保证事务隔离）
            // 在 WPF 应用中，通常每个 ViewModel 操作创建一个 Scope：
            //   using var scope = _serviceProvider.CreateScope();
            //   var repo = scope.ServiceProvider.GetRequiredService<IAlarmRepository>();
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(sqliteConnectionString, sqliteOptions =>
                {
                    // 配置 SQLite 特定选项：
                    // CommandTimeout = 30 秒（防止复杂查询无限等待）
                    sqliteOptions.CommandTimeout(30);
                });

                // 开发环境启用详细错误（生产环境建议关闭以避免敏感信息泄露）
#if DEBUG
                options.EnableSensitiveDataLogging(true);
                options.EnableDetailedErrors(true);
#endif
            });

            // ================================================================
            // 2. 仓储层注册（Scoped，与 DbContext 同生命周期）
            // ================================================================

            // 泛型仓储：如果某个实体没有专用仓储，可直接注入 IRepository<T>
            services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));

            // 报警专用仓储：包含报警业务特定查询（GetActiveAlarmsAsync 等）
            services.AddScoped<IAlarmRepository, AlarmRepository>();

            // 用户专用仓储：包含密码认证（AuthenticateAsync）等安全相关操作
            services.AddScoped<IUserRepository, UserRepository>();

            // ================================================================
            // 3. 事件总线（Singleton：整个应用生命周期共享，是模块通信中心）
            // ================================================================

            // IEventBus 接口绑定到 EventBusService 实现
            // EventBusService 同时对外和对内（基础设施层内部）使用
            services.AddSingleton<EventBusService>();
            services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<EventBusService>());

            // ================================================================
            // 4. 日志服务（Singleton：全局共享 Serilog 实例）
            // ================================================================

            services.AddSingleton<SerilogService>(sp =>
                new SerilogService(
                    logDirectory: logDirectory,
                    maxBufferSize: 1000,
                    initialLevel: Domain.Enums.LogLevel.Info));

            // ILogService 接口绑定（供 Domain/Application 层注入 ILogService 使用）
            services.AddSingleton<ILogService>(sp => sp.GetRequiredService<SerilogService>());

            // ================================================================
            // 5. 文件服务（Singleton：无状态工具类，线程安全可共享）
            // ================================================================

            services.AddSingleton<IFileService>(sp =>
                new FileService(
                    eventBus: sp.GetRequiredService<IEventBus>(),
                    safeRootDirectory: safeDeleteRootDirectory));

            // ================================================================
            // 6. JSON 配置服务（Singleton：全局配置，热重载后通知所有订阅者）
            // ================================================================

            services.AddSingleton(sp => new JsonConfigService(configFilePath));

            // ================================================================
            // 7. HTTP 客户端服务（通过 HttpClientFactory 管理）
            // ================================================================

            // 注册 HttpClientFactory（Microsoft.Extensions.Http 提供）
            // 命名客户端 "SmartIndustryApi" 配置基地址和超时
            services.AddHttpClient("SmartIndustryApi", client =>
            {
                // 基地址：从配置服务读取（此处使用占位符，生产中从 IConfiguration 读取）
                // 超时：30秒（工业网络环境下适当放宽）
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("User-Agent", "SmartIndustry-Platform/1.0");
            });

            // 注册 HttpClientService（Singleton，内部 HttpClient 由 Factory 管理生命周期）
            // 注意：IHttpClientService 接口已由 Domain 层的 ICommunicationService 等替代，
            // 此处直接注册具体类，调用方可按需注入 HttpClientService
            services.AddSingleton<HttpClientService>();

            return services;
        }

        /// <summary>
        /// 在应用启动时确保数据库已创建（执行待处理的 EF Core 迁移）。
        /// 在 WPF App.OnStartup 或 Hosted Service 的 StartAsync 中调用。
        /// </summary>
        /// <param name="serviceProvider">DI 服务提供者</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task EnsureDatabaseCreatedAsync(
            this IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            // 创建独立 Scope（避免在 Singleton 范围内使用 Scoped 的 DbContext）
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 应用所有待处理的迁移（如果数据库不存在则创建）
            // 等效于 dotnet ef database update，但在运行时执行
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
    }
}
