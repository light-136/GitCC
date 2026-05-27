// ============================================================
// 文件：ServiceCollectionExtensions.cs
// 用途：依赖注入扩展方法 — 集中注册所有服务
// 设计思路：
//   每个层提供自己的扩展方法，UI层在启动时调用。
//   这样每个层管理自己的注册逻辑，UI层无需了解具体实现类。
// ============================================================

using Microsoft.Extensions.DependencyInjection;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Infrastructure.EventBus;
using SmartSemiCon.Infrastructure.Logging;
using SmartSemiCon.Infrastructure.SecsGem;

namespace SmartSemiCon.Infrastructure.DependencyInjection
{
    /// <summary>
    /// 基础设施层DI注册扩展方法。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册基础设施层所有服务。
        /// </summary>
        public static IServiceCollection AddInfrastructure(this IServiceCollection services)
        {
            // 事件总线 — 单例（全局唯一）
            services.AddSingleton<IEventBus, EventBusService>();

            // 日志服务 — 单例
            services.AddSingleton<ILogService, LogService>();

            // SECS/GEM服务 — 单例
            services.AddSingleton<ISecsGemService, SecsGemService>();

            return services;
        }
    }
}
