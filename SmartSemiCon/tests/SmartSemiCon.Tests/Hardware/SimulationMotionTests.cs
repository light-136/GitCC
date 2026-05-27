// ============================================================
// 文件：SimulationMotionTests.cs
// 用途：运动控制模块单元测试
// 设计思路：
//   运动控制是半导体设备的核心，测试覆盖：
//   1. AxisManager — 轴配置管理和轴查找
//   2. SimulationMotionCard — 模拟控制卡初始化
//   3. SimulationAxisController — 使能/回原点等基本操作
//
//   使用 Moq 模拟 IEventBus（AxisManager 的依赖），
//   但 SimulationMotionCard 和 SimulationAxisController 使用真实实例，
//   验证模拟运动控制的真实逻辑。
// ============================================================

using Moq;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Events;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;
using SmartSemiCon.Hardware.Motion.Axis;
using SmartSemiCon.Hardware.Motion.Drivers;

namespace SmartSemiCon.Tests.Hardware
{
    /// <summary>
    /// 运动控制模块测试类 — 验证 AxisManager、SimulationMotionCard 和
    /// SimulationAxisController 的核心功能。
    /// </summary>
    public class SimulationMotionTests
    {
        // 共用的 Mock IEventBus
        private readonly Mock<IEventBus> _mockEventBus;

        /// <summary>
        /// 构造函数 — 初始化测试依赖。
        /// </summary>
        public SimulationMotionTests()
        {
            _mockEventBus = new Mock<IEventBus>();
        }

        // =============================================
        // AxisManager 配置轴测试
        // =============================================

        /// <summary>
        /// 验证 AxisManager.Configure 能正确加载轴配置。
        /// 配置后 AxisCount 应等于配置的轴数。
        /// </summary>
        [Fact]
        public void AxisManager_Configure_ShouldSetAxisCount()
        {
            // Arrange
            var manager = new AxisManager(_mockEventBus.Object);
            var configs = new List<AxisConfig>
            {
                new AxisConfig { AxisId = 0, Name = "X轴", CardId = 0, CardAxisIndex = 0 },
                new AxisConfig { AxisId = 1, Name = "Y轴", CardId = 0, CardAxisIndex = 1 },
                new AxisConfig { AxisId = 2, Name = "Z轴", CardId = 0, CardAxisIndex = 2 }
            };

            // Act
            manager.Configure(configs);

            // Assert — 应有3个轴
            Assert.Equal(3, manager.AxisCount);
        }

        /// <summary>
        /// 验证 AxisManager.CreateDefaultConfigs 返回30轴的默认配置。
        /// </summary>
        [Fact]
        public void AxisManager_CreateDefaultConfigs_ShouldReturn30Axes()
        {
            var configs = AxisManager.CreateDefaultConfigs();

            Assert.Equal(30, configs.Count);
        }

        /// <summary>
        /// 验证 AxisManager.CreateDefaultConfigs 的轴ID从0到29连续编号。
        /// </summary>
        [Fact]
        public void AxisManager_CreateDefaultConfigs_AxisIdsShouldBe0To29()
        {
            var configs = AxisManager.CreateDefaultConfigs();

            for (int i = 0; i < 30; i++)
            {
                Assert.Equal(i, configs[i].AxisId);
            }
        }

        /// <summary>
        /// 验证 AxisManager 使用默认配置时，所有轴的卡类型都是 Simulation。
        /// </summary>
        [Fact]
        public void AxisManager_CreateDefaultConfigs_AllShouldBeSimulation()
        {
            var configs = AxisManager.CreateDefaultConfigs();

            Assert.All(configs, c => Assert.Equal(MotionCardType.Simulation, c.CardType));
        }

        /// <summary>
        /// 验证 AxisManager.Configure 能处理多张控制卡的轴配置。
        /// 默认配置中30轴分布在4张卡上（每10轴一张卡）。
        /// </summary>
        [Fact]
        public void AxisManager_Configure_MultipleCards_ShouldWork()
        {
            var manager = new AxisManager(_mockEventBus.Object);
            var configs = AxisManager.CreateDefaultConfigs();

            // Act — 不应抛出异常
            var exception = Record.Exception(() => manager.Configure(configs));

            Assert.Null(exception);
            Assert.Equal(30, manager.AxisCount);
        }

        // =============================================
        // AxisManager 获取轴控制器测试
        // =============================================

        /// <summary>
        /// 验证 AxisManager.GetAxis 能获取已配置的轴控制器。
        /// </summary>
        [Fact]
        public void AxisManager_GetAxis_ExistingId_ShouldReturnController()
        {
            var manager = new AxisManager(_mockEventBus.Object);
            manager.Configure(new List<AxisConfig>
            {
                new AxisConfig { AxisId = 0, Name = "X轴", CardId = 0, CardAxisIndex = 0 }
            });

            var axis = manager.GetAxis(0);

            Assert.NotNull(axis);
        }

        /// <summary>
        /// 验证 AxisManager.GetAxis 对不存在的轴ID返回 null。
        /// </summary>
        [Fact]
        public void AxisManager_GetAxis_NonExistingId_ShouldReturnNull()
        {
            var manager = new AxisManager(_mockEventBus.Object);
            manager.Configure(new List<AxisConfig>
            {
                new AxisConfig { AxisId = 0, Name = "X轴", CardId = 0, CardAxisIndex = 0 }
            });

            var axis = manager.GetAxis(999); // 不存在的轴ID

            Assert.Null(axis);
        }

        /// <summary>
        /// 验证 AxisManager.GetAxis 返回的控制器具有正确的配置信息。
        /// </summary>
        [Fact]
        public void AxisManager_GetAxis_ShouldHaveCorrectConfig()
        {
            var manager = new AxisManager(_mockEventBus.Object);
            manager.Configure(new List<AxisConfig>
            {
                new AxisConfig
                {
                    AxisId = 5,
                    Name = "Theta轴",
                    CardId = 0,
                    CardAxisIndex = 5,
                    MaxVelocity = 360.0
                }
            });

            var axis = manager.GetAxis(5);

            Assert.NotNull(axis);
            Assert.Equal(5, axis!.Config.AxisId);
            Assert.Equal("Theta轴", axis.Config.Name);
            Assert.Equal(360.0, axis.Config.MaxVelocity);
        }

        /// <summary>
        /// 使用 Theory 测试：从30轴配置中按ID查找轴。
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(29)]
        public void AxisManager_GetAxis_FromDefaultConfigs_ShouldReturnAxis(int axisId)
        {
            var manager = new AxisManager(_mockEventBus.Object);
            manager.Configure(AxisManager.CreateDefaultConfigs());

            var axis = manager.GetAxis(axisId);

            Assert.NotNull(axis);
        }

        // =============================================
        // SimulationMotionCard 初始化测试
        // =============================================

        /// <summary>
        /// 验证 SimulationMotionCard 构造后的初始属性。
        /// </summary>
        [Fact]
        public void SimulationMotionCard_Constructor_ShouldSetProperties()
        {
            var card = new SimulationMotionCard(cardId: 1);

            Assert.Equal(1, card.CardId);
            Assert.Equal(MotionCardType.Simulation, card.CardType);
            Assert.Equal(32, card.MaxAxisCount);
        }

        /// <summary>
        /// 验证 SimulationMotionCard.InitializeAsync 应返回 true。
        /// </summary>
        [Fact]
        public async Task SimulationMotionCard_InitializeAsync_ShouldReturnTrue()
        {
            var card = new SimulationMotionCard(cardId: 0);

            var result = await card.InitializeAsync();

            Assert.True(result);
        }

        /// <summary>
        /// 验证 SimulationMotionCard.InitializeAsync 重复调用应幂等（返回true）。
        /// </summary>
        [Fact]
        public async Task SimulationMotionCard_InitializeAsync_CalledTwice_ShouldStillReturnTrue()
        {
            var card = new SimulationMotionCard(cardId: 0);

            await card.InitializeAsync();
            var result = await card.InitializeAsync();

            Assert.True(result);
        }

        /// <summary>
        /// 验证 SimulationMotionCard.GetAxis 能获取轴控制器。
        /// 即使轴未显式配置，也应自动创建默认轴。
        /// </summary>
        [Fact]
        public void SimulationMotionCard_GetAxis_ShouldReturnAxisController()
        {
            var card = new SimulationMotionCard(cardId: 0);

            var axis = card.GetAxis(0);

            Assert.NotNull(axis);
        }

        /// <summary>
        /// 验证 SimulationMotionCard.ConfigureAxis 配置后能获取到正确的轴。
        /// </summary>
        [Fact]
        public void SimulationMotionCard_ConfigureAxis_ShouldBeRetrievable()
        {
            var card = new SimulationMotionCard(cardId: 0);
            var config = new AxisConfig
            {
                AxisId = 0,
                Name = "测试轴",
                CardId = 0,
                CardAxisIndex = 0,
                MaxVelocity = 500.0
            };

            card.ConfigureAxis(config);
            var axis = card.GetAxis(0);

            Assert.Equal("测试轴", axis.Config.Name);
            Assert.Equal(500.0, axis.Config.MaxVelocity);
        }

        // =============================================
        // SimulationAxisController 使能/回原测试
        // =============================================

        /// <summary>
        /// 验证 SimulationAxisController 新建后的初始状态为 NotReady，未使能。
        /// </summary>
        [Fact]
        public void SimulationAxisController_InitialState_ShouldBeNotReady()
        {
            var config = new AxisConfig { AxisId = 0, Name = "X轴" };
            var controller = new SimulationAxisController(config);

            Assert.Equal(AxisState.NotReady, controller.Status.State);
            Assert.False(controller.Status.IsServoOn);
            Assert.False(controller.Status.IsHomed);
        }

        /// <summary>
        /// 验证 ServoOnAsync 使能后，轴状态变为 Ready，IsServoOn 为 true。
        /// </summary>
        [Fact]
        public async Task SimulationAxisController_ServoOn_ShouldSetReadyState()
        {
            var config = new AxisConfig { AxisId = 0, Name = "X轴" };
            var controller = new SimulationAxisController(config);

            var result = await controller.ServoOnAsync();

            Assert.True(result);
            Assert.True(controller.Status.IsServoOn);
            Assert.Equal(AxisState.Ready, controller.Status.State);
        }

        /// <summary>
        /// 验证 ServoOffAsync 关闭使能后，状态变为 Disabled。
        /// </summary>
        [Fact]
        public async Task SimulationAxisController_ServoOff_ShouldSetDisabledState()
        {
            var config = new AxisConfig { AxisId = 0, Name = "X轴" };
            var controller = new SimulationAxisController(config);

            await controller.ServoOnAsync();
            var result = await controller.ServoOffAsync();

            Assert.True(result);
            Assert.False(controller.Status.IsServoOn);
            Assert.Equal(AxisState.Disabled, controller.Status.State);
        }

        /// <summary>
        /// 验证 HomeAsync 回原点完成后，IsHomed 应为 true，位置应在原点偏移处。
        /// </summary>
        [Fact]
        public async Task SimulationAxisController_Home_ShouldSetHomedAndPosition()
        {
            var config = new AxisConfig
            {
                AxisId = 0,
                Name = "X轴",
                HomeOffset = 0.0,
                HomeVelocity = 100.0, // 高速回原以减少测试时间
                MaxAcceleration = 5000.0,
                MaxDeceleration = 5000.0
            };
            var controller = new SimulationAxisController(config);

            // 先使能
            await controller.ServoOnAsync();

            // 回原点
            var result = await controller.HomeAsync();

            Assert.True(result);
            Assert.True(controller.Status.IsHomed);
            Assert.Equal(0.0, controller.Status.Position, precision: 3);
        }

        /// <summary>
        /// 验证未使能时调用 HomeAsync 应返回 false。
        /// 安全性要求：未使能的轴不能执行任何运动。
        /// </summary>
        [Fact]
        public async Task SimulationAxisController_Home_WithoutServoOn_ShouldReturnFalse()
        {
            var config = new AxisConfig { AxisId = 0, Name = "X轴" };
            var controller = new SimulationAxisController(config);

            var result = await controller.HomeAsync();

            Assert.False(result);
            Assert.False(controller.Status.IsHomed);
        }

        /// <summary>
        /// 验证 ClearAlarmAsync 清除报警后，报警代码归零，状态恢复。
        /// </summary>
        [Fact]
        public async Task SimulationAxisController_ClearAlarm_ShouldResetAlarmCode()
        {
            var config = new AxisConfig { AxisId = 0, Name = "X轴" };
            var controller = new SimulationAxisController(config);

            // 先使能
            await controller.ServoOnAsync();

            // 清除报警
            await controller.ClearAlarmAsync();

            Assert.Equal(0, controller.Status.AlarmCode);
            Assert.Equal(AxisState.Ready, controller.Status.State);
        }

        /// <summary>
        /// 使用 Theory 测试不同卡ID的 SimulationMotionCard 构造。
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        public void SimulationMotionCard_DifferentCardIds_ShouldRetainId(int cardId)
        {
            var card = new SimulationMotionCard(cardId);

            Assert.Equal(cardId, card.CardId);
        }
    }
}
