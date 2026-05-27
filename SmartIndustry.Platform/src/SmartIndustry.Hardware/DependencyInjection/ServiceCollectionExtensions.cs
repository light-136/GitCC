// ============================================================
// 文件：ServiceCollectionExtensions.cs
// 层级：硬件抽象层（Hardware Layer）> DependencyInjection
// 职责：硬件抽象层的依赖注入注册扩展方法。
//       提供 AddHardware() 扩展方法，一次性完成所有硬件服务的注册，
//       Application 层和 UI 层只需调用一次即可使用所有硬件功能。
//
// 注册策略：
//   - 运动控制卡：Singleton（整个应用生命周期内唯一实例）
//   - 视觉引擎：Singleton（相机连接在整个应用期间保持）
//   - IO管理器：Singleton（IO状态需全局一致）
//   - 标定服务：Singleton（标定数据全局共享）
//   - AxisManager：Singleton（轴状态全局唯一）
//   - MotionScheduler：Singleton（调度器全局唯一）
//   - VisionMotionCoordinator：Singleton（协调器引用其他单例）
//
// 配置通过 HardwareOptions 传入，支持：
//   - 选择仿真模式或真实硬件
//   - 配置轴数量、轴名称
//   - 配置视觉引擎类型
//   - 配置IO轮询周期
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Models;
using SmartIndustry.Hardware.Motion;
using SmartIndustry.Hardware.Motion.Drivers;
using SmartIndustry.Hardware.Motion.Interpolation;
using SmartIndustry.Hardware.Motion.Profiles;
using SmartIndustry.Hardware.Motion.Scheduler;
using SmartIndustry.Hardware.Vision.Calibration;
using SmartIndustry.Hardware.Vision.Engines;
using SmartIndustry.Hardware.IO.Digital;
using SmartIndustry.Hardware.IO.Simulation;
using SmartIndustry.Hardware.Coordination;

namespace SmartIndustry.Hardware.DependencyInjection
{
    /// <summary>
    /// 硬件层配置选项 — 控制各硬件模块的初始化参数。
    /// </summary>
    public class HardwareOptions
    {
        /// <summary>是否使用仿真模式（true=无需真实硬件，用于开发测试）</summary>
        public bool UseSimulation { get; set; } = true;

        // ---- 运动控制配置 ----

        /// <summary>轴名称列表（决定创建的轴控制器数量和标识）</summary>
        public string[] AxisNames { get; set; } = { "X", "Y", "Z" };

        /// <summary>运动控制卡支持的最大轴数（仿真模式下的轴数上限）</summary>
        public int MaxAxisCount { get; set; } = 30;

        /// <summary>默认最大速度（mm/s）</summary>
        public double DefaultMaxVelocity { get; set; } = 200.0;

        /// <summary>默认加速度（mm/s²）</summary>
        public double DefaultAcceleration { get; set; } = 500.0;

        /// <summary>默认 Jerk（mm/s³，S曲线规划使用）</summary>
        public double DefaultJerk { get; set; } = 5000.0;

        /// <summary>运动超时时间（ms）</summary>
        public int MoveTimeoutMs { get; set; } = 30000;

        // ---- 视觉配置 ----

        /// <summary>视觉引擎ID列表（对应视觉工位数量）</summary>
        public string[] VisionEngineIds { get; set; } = { "Camera0" };

        /// <summary>模拟图像宽度（像素）</summary>
        public int SimulationImageWidth { get; set; } = 640;

        /// <summary>模拟图像高度（像素）</summary>
        public int SimulationImageHeight { get; set; } = 480;

        // ---- IO配置 ----

        /// <summary>IO设备ID</summary>
        public string IoDeviceId { get; set; } = "SimIO_0";

        /// <summary>IO轮询周期（ms）</summary>
        public int IoPollPeriodMs { get; set; } = 10;

        /// <summary>默认消抖时间（ms）</summary>
        public int DefaultDebounceMs { get; set; } = 10;

        // ---- 协同配置 ----

        /// <summary>对位精度容差（mm，小于此值不补偿）</summary>
        public double AlignmentTolerance { get; set; } = 0.05;

        /// <summary>最大补偿范围（mm，超过此值视为异常）</summary>
        public double MaxCorrectionRange { get; set; } = 10.0;
    }

    /// <summary>
    /// 硬件层 DI 注册扩展方法。
    /// 在 Program.cs 或 App.xaml.cs 中调用：
    ///   services.AddHardware(options => { options.UseSimulation = true; });
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册所有硬件抽象层服务。
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">配置选项委托</param>
        /// <returns>服务集合（支持链式调用）</returns>
        public static IServiceCollection AddHardware(
            this IServiceCollection services,
            Action<HardwareOptions>? configureOptions = null)
        {
            // 构建配置选项
            var options = new HardwareOptions();
            configureOptions?.Invoke(options);

            // 注册配置选项本身（供其他服务注入）
            services.AddSingleton(options);

            // 按模块分组注册
            services
                .AddMotionControl(options)
                .AddVisionControl(options)
                .AddIoControl(options)
                .AddCoordination(options);

            return services;
        }

        // ==================== 运动控制模块 ====================

        /// <summary>
        /// 注册运动控制相关服务（运动卡、轴控制器、调度器、插补器、速度规划）
        /// </summary>
        private static IServiceCollection AddMotionControl(
            this IServiceCollection services, HardwareOptions options)
        {
            // 1. 运动控制卡（仿真或真实）
            if (options.UseSimulation)
            {
                services.AddSingleton<IMotionCard>(sp =>
                    new SimulationMotionCard("SimCard0", options.MaxAxisCount));
            }
            // 真实卡注册示例（实际项目中取消注释）：
            // else { services.AddSingleton<IMotionCard, LeisaiMotionCard>(); }

            // 2. 速度规划器
            services.AddSingleton<TrapezoidalProfile>();
            services.AddSingleton<SCurveProfile>();

            // 3. 插补器
            services.AddSingleton<LinearInterpolator>();
            services.AddSingleton<CircularInterpolator>();

            // 4. AxisManager（依赖 IMotionCard 和 IEventBus）
            services.AddSingleton(sp =>
            {
                var motionCard = sp.GetRequiredService<IMotionCard>();
                var eventBus = sp.GetRequiredService<IEventBus>();

                var manager = new AxisManager(eventBus);

                var defaultParams = new MotionParameters
                {
                    MaxVelocity = options.DefaultMaxVelocity,
                    Acceleration = options.DefaultAcceleration,
                    Deceleration = options.DefaultAcceleration,
                    Jerk = options.DefaultJerk
                };

                // 注册配置中指定的所有轴
                for (int i = 0; i < options.AxisNames.Length; i++)
                {
                    string axisId = options.AxisNames[i];
                    var controller = new AxisController(
                        motionCard, i, axisId, eventBus,
                        motionParams: defaultParams,
                        moveTimeoutMs: options.MoveTimeoutMs);
                    manager.RegisterAxis(axisId, controller);
                }

                return manager;
            });

            // 5. MotionScheduler（从 AxisManager 创建）
            services.AddSingleton(sp =>
            {
                var manager = sp.GetRequiredService<AxisManager>();
                return manager.CreateScheduler();
            });

            return services;
        }

        // ==================== 视觉控制模块 ====================

        /// <summary>
        /// 注册视觉引擎和标定服务
        /// </summary>
        private static IServiceCollection AddVisionControl(
            this IServiceCollection services, HardwareOptions options)
        {
            // 1. 标定服务（全局唯一，持久化标定数据）
            services.AddSingleton<CalibrationService>();

            // 2. 视觉引擎字典（支持多相机，Key=EngineId）
            services.AddSingleton(sp =>
            {
                var eventBus = sp.GetRequiredService<IEventBus>();
                var engines = new Dictionary<string, IVisionEngine>();

                foreach (var engineId in options.VisionEngineIds)
                {
                    IVisionEngine engine;
                    if (options.UseSimulation)
                    {
                        engine = new SimulationVisionEngine(
                            engineId, eventBus,
                            options.SimulationImageWidth,
                            options.SimulationImageHeight);
                    }
                    else
                    {
                        // 真实视觉引擎注册示例：
                        // engine = new HalconVisionEngine(engineId, eventBus);
                        engine = new SimulationVisionEngine(engineId, eventBus);
                    }
                    engines[engineId] = engine;
                }
                return engines;
            });

            // 3. 默认视觉引擎（取第一个，方便单相机场景注入）
            services.AddSingleton<IVisionEngine>(sp =>
            {
                var engines = sp.GetRequiredService<Dictionary<string, IVisionEngine>>();
                if (engines.Count == 0)
                    throw new InvalidOperationException("未配置任何视觉引擎");
                return engines.Values.First();
            });

            return services;
        }

        // ==================== IO 控制模块 ====================

        /// <summary>
        /// 注册 IO 设备和管理器
        /// </summary>
        private static IServiceCollection AddIoControl(
            this IServiceCollection services, HardwareOptions options)
        {
            // 1. IO 设备（仿真或真实）
            if (options.UseSimulation)
            {
                services.AddSingleton<SimulatedIoDevice>(sp =>
                    new SimulatedIoDevice(options.IoDeviceId));

                services.AddSingleton<IDigitalIoDevice>(sp =>
                    sp.GetRequiredService<SimulatedIoDevice>());
            }
            // 真实 Modbus IO 设备注册示例：
            // else { services.AddSingleton<IDigitalIoDevice, ModbusIoDevice>(); }

            // 2. 数字 IO 管理器
            services.AddSingleton(sp =>
            {
                var ioDevice = sp.GetRequiredService<IDigitalIoDevice>();
                var eventBus = sp.GetRequiredService<IEventBus>();
                var manager = new DigitalIoManager(ioDevice, eventBus, options.IoDeviceId);

                // 注册默认通道（16路DI + 16路DO）
                var defaultChannels = new List<DebouncedChannelConfig>();
                for (int i = 0; i < 16; i++)
                {
                    defaultChannels.Add(new DebouncedChannelConfig
                    {
                        Address = i,
                        Name = $"DI{i:D2}",
                        Type = Domain.Models.IoChannelType.DigitalInput,
                        DebounceTimeMs = options.DefaultDebounceMs
                    });
                    defaultChannels.Add(new DebouncedChannelConfig
                    {
                        Address = 100 + i,
                        Name = $"DO{i:D2}",
                        Type = Domain.Models.IoChannelType.DigitalOutput,
                        DebounceTimeMs = 0 // 输出不需要消抖
                    });
                }
                manager.RegisterChannels(defaultChannels);
                manager.StartPolling(options.IoPollPeriodMs);

                return manager;
            });

            return services;
        }

        // ==================== 协同控制模块 ====================

        /// <summary>
        /// 注册视觉-运动协同控制器
        /// </summary>
        private static IServiceCollection AddCoordination(
            this IServiceCollection services, HardwareOptions options)
        {
            services.AddSingleton(sp =>
            {
                var axisManager = sp.GetRequiredService<AxisManager>();
                var visionEngines = sp.GetRequiredService<Dictionary<string, IVisionEngine>>();
                var calibrationService = sp.GetRequiredService<CalibrationService>();
                var eventBus = sp.GetRequiredService<IEventBus>();

                // 从 AxisManager 提取轴控制器字典
                var axisMap = axisManager.AxisIds
                    .ToDictionary(id => id, id => axisManager.GetAxis(id)!);

                var coordinator = new VisionMotionCoordinator(
                    visionEngines, axisMap, calibrationService, eventBus);

                coordinator.AlignmentTolerance = options.AlignmentTolerance;
                coordinator.MaxCorrectionRange = options.MaxCorrectionRange;

                return coordinator;
            });

            return services;
        }
    }
}
