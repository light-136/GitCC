// ============================================================
// 文件：ServiceCollectionExtensions.cs
// 层次：应用层 (Application Layer) — DI 注册
// 职责：一键注册 Application 层所有服务到 DI 容器
// ============================================================

using Microsoft.Extensions.DependencyInjection;
using SmartIndustry.Application.Alarm;
using SmartIndustry.Application.Automation;
using SmartIndustry.Application.Recipe;
using SmartIndustry.Application.SecsGem;
using SmartIndustry.Application.StateMachine;
using SmartIndustry.Application.TaskScheduler;
using SmartIndustry.Application.User;
using SmartIndustry.Domain.Interfaces;

namespace SmartIndustry.Application.DependencyInjection
{
    /// <summary>
    /// Application 层 DI 注册扩展方法
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 Application 层所有服务到 DI 容器
        /// </summary>
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            // 设备状态机（全局单例）
            services.AddSingleton<DeviceStateMachine>();

            // 报警服务（单例，维护内存缓存 + 接口映射）
            services.AddSingleton<AlarmService>();
            services.AddSingleton<IAlarmService>(sp => sp.GetRequiredService<AlarmService>());

            // 配方服务（Scoped，依赖 DbContext + 接口映射）
            services.AddScoped<RecipeService>();
            services.AddScoped<IRecipeService>(sp => sp.GetRequiredService<RecipeService>());

            // 用户服务（单例，维护 Token 缓存 + 接口映射）
            services.AddSingleton<UserService>();
            services.AddSingleton<IUserService>(sp => sp.GetRequiredService<UserService>());

            // 任务调度器（单例）
            services.AddSingleton<AppTaskScheduler>();

            // SECS/GEM 服务（单例）
            services.AddSingleton<SecsGemService>();

            // 自动化流程服务（单例）
            services.AddSingleton<AutomationService>();

            return services;
        }
    }
}
