// ============================================================
// 文件：StateMachineTests.cs
// 用途：设备状态机单元测试
// 设计思路：
//   状态机是工业设备的灵魂组件，所有操作都依赖正确的状态。
//   测试覆盖：
//   1. 初始状态 — 设备上电后应为 Idle
//   2. 有效状态转换 — 按照规则成功转换
//   3. 无效状态转换 — 违反规则的转换应被拒绝
//
//   使用 Moq 模拟 IEventBus 和 IAlarmService。
//   DeviceStateMachine 的构造函数需要 (IEventBus, IAlarmService)。
//   测试真实的状态转换逻辑。
// ============================================================

using Moq;
using SmartSemiCon.Application.StateMachine;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Events;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.Tests.Application
{
    /// <summary>
    /// 设备状态机测试类 — 验证 DeviceStateMachine 的状态转换逻辑。
    /// </summary>
    public class StateMachineTests
    {
        // Mock 依赖
        private readonly Mock<IEventBus> _mockEventBus;
        private readonly Mock<IAlarmService> _mockAlarmService;

        /// <summary>
        /// 构造函数 — 初始化测试夹具。
        /// 默认设置 ActiveAlarms 为空列表（无报警），以便大多数测试通过守卫条件。
        /// </summary>
        public StateMachineTests()
        {
            _mockEventBus = new Mock<IEventBus>();
            _mockAlarmService = new Mock<IAlarmService>();

            // 默认无活跃报警（状态转换的守卫条件需要无报警）
            _mockAlarmService.Setup(a => a.ActiveAlarms)
                .Returns(new List<AlarmRecord>().AsReadOnly());
        }

        /// <summary>
        /// 辅助方法 — 创建一个新的 DeviceStateMachine 实例。
        /// </summary>
        private DeviceStateMachine CreateStateMachine()
        {
            return new DeviceStateMachine(_mockEventBus.Object, _mockAlarmService.Object);
        }

        // =============================================
        // 初始状态测试
        // =============================================

        /// <summary>
        /// 验证状态机创建后的初始状态为 Idle（空闲）。
        /// 设备上电后应处于空闲状态，等待操作员初始化。
        /// </summary>
        [Fact]
        public void StateMachine_InitialState_ShouldBeIdle()
        {
            var sm = CreateStateMachine();

            Assert.Equal(DeviceState.Idle, sm.CurrentState);
        }

        /// <summary>
        /// 验证初始状态下可用的触发命令列表。
        /// Idle 状态应可以执行 Initialize、StartManual、EmergencyStop、EnterMaintenance。
        /// </summary>
        [Fact]
        public void StateMachine_IdleState_ShouldHaveAvailableTriggers()
        {
            var sm = CreateStateMachine();

            var triggers = sm.GetAvailableTriggers();

            Assert.Contains("Initialize", triggers);
            Assert.Contains("EmergencyStop", triggers);
        }

        // =============================================
        // 有效状态转换测试
        // =============================================

        /// <summary>
        /// 验证 Idle → Init 转换（触发命令 "Initialize"）。
        /// 这是设备初始化流程的第一步。
        /// </summary>
        [Fact]
        public async Task StateMachine_IdleToInit_ShouldSucceed()
        {
            var sm = CreateStateMachine();

            var result = await sm.FireAsync("Initialize");

            Assert.True(result);
            Assert.Equal(DeviceState.Init, sm.CurrentState);
        }

        /// <summary>
        /// 验证 Init → Auto 转换（触发命令 "StartAuto"）。
        /// 初始化完成后进入自动运行模式。
        /// </summary>
        [Fact]
        public async Task StateMachine_InitToAuto_ShouldSucceed()
        {
            var sm = CreateStateMachine();
            await sm.FireAsync("Initialize"); // Idle → Init

            var result = await sm.FireAsync("StartAuto");

            Assert.True(result);
            Assert.Equal(DeviceState.Auto, sm.CurrentState);
        }

        /// <summary>
        /// 验证 Init → Manual 转换（触发命令 "StartManual"）。
        /// 初始化完成后进入手动操作模式。
        /// </summary>
        [Fact]
        public async Task StateMachine_InitToManual_ShouldSucceed()
        {
            var sm = CreateStateMachine();
            await sm.FireAsync("Initialize");

            var result = await sm.FireAsync("StartManual");

            Assert.True(result);
            Assert.Equal(DeviceState.Manual, sm.CurrentState);
        }

        /// <summary>
        /// 验证 Auto → Paused 转换（触发命令 "Pause"）。
        /// 自动运行中暂停。
        /// </summary>
        [Fact]
        public async Task StateMachine_AutoToPaused_ShouldSucceed()
        {
            var sm = CreateStateMachine();
            await sm.FireAsync("Initialize");
            await sm.FireAsync("StartAuto");

            var result = await sm.FireAsync("Pause");

            Assert.True(result);
            Assert.Equal(DeviceState.Paused, sm.CurrentState);
        }

        /// <summary>
        /// 验证 Paused → Auto 转换（触发命令 "Resume"）。
        /// 暂停后恢复自动运行。
        /// </summary>
        [Fact]
        public async Task StateMachine_PausedToAuto_ShouldSucceed()
        {
            var sm = CreateStateMachine();
            await sm.FireAsync("Initialize");
            await sm.FireAsync("StartAuto");
            await sm.FireAsync("Pause");

            var result = await sm.FireAsync("Resume");

            Assert.True(result);
            Assert.Equal(DeviceState.Auto, sm.CurrentState);
        }

        /// <summary>
        /// 验证 Auto → Idle 转换（触发命令 "Stop"）。
        /// 停止自动运行，回到空闲状态。
        /// </summary>
        [Fact]
        public async Task StateMachine_AutoToIdle_ShouldSucceed()
        {
            var sm = CreateStateMachine();
            await sm.FireAsync("Initialize");
            await sm.FireAsync("StartAuto");

            var result = await sm.FireAsync("Stop");

            Assert.True(result);
            Assert.Equal(DeviceState.Idle, sm.CurrentState);
        }

        /// <summary>
        /// 验证 Auto → Alarm 转换（触发命令 "Alarm"）。
        /// 自动运行中检测到故障。
        /// </summary>
        [Fact]
        public async Task StateMachine_AutoToAlarm_ShouldSucceed()
        {
            var sm = CreateStateMachine();
            await sm.FireAsync("Initialize");
            await sm.FireAsync("StartAuto");

            var result = await sm.FireAsync("Alarm");

            Assert.True(result);
            Assert.Equal(DeviceState.Alarm, sm.CurrentState);
        }

        /// <summary>
        /// 验证 Alarm → Idle 转换（触发命令 "Reset"），前提是无活跃报警。
        /// 报警清除后复位回空闲状态。
        /// </summary>
        [Fact]
        public async Task StateMachine_AlarmToIdle_WhenNoActiveAlarms_ShouldSucceed()
        {
            var sm = CreateStateMachine();
            await sm.FireAsync("Initialize");
            await sm.FireAsync("StartAuto");
            await sm.FireAsync("Alarm");

            // 确保无活跃报警（已在构造函数中设置）
            var result = await sm.FireAsync("Reset");

            Assert.True(result);
            Assert.Equal(DeviceState.Idle, sm.CurrentState);
        }

        /// <summary>
        /// 验证任何状态都可以转换到 EmergencyStop（急停）。
        /// </summary>
        [Fact]
        public async Task StateMachine_AnyStateToEmergencyStop_ShouldSucceed()
        {
            var sm = CreateStateMachine();
            await sm.FireAsync("Initialize");
            await sm.FireAsync("StartAuto"); // 现在是 Auto 状态

            var result = await sm.FireAsync("EmergencyStop");

            Assert.True(result);
            Assert.Equal(DeviceState.EmergencyStop, sm.CurrentState);
        }

        /// <summary>
        /// 验证状态转换时，应通过事件总线发布 DeviceStateChangedEvent。
        /// </summary>
        [Fact]
        public async Task StateMachine_Transition_ShouldPublishStateChangedEvent()
        {
            var sm = CreateStateMachine();

            await sm.FireAsync("Initialize");

            _mockEventBus.Verify(
                eb => eb.Publish(It.Is<DeviceStateChangedEvent>(
                    e => e.PreviousState == DeviceState.Idle
                      && e.CurrentState == DeviceState.Init
                      && e.Trigger == "Initialize")),
                Times.Once);
        }

        /// <summary>
        /// 验证状态转换时，StateChanged 事件应被触发。
        /// </summary>
        [Fact]
        public async Task StateMachine_Transition_ShouldRaiseStateChangedEvent()
        {
            var sm = CreateStateMachine();
            (DeviceState From, DeviceState To, string Trigger)? received = null;

            sm.StateChanged += (_, args) => received = args;

            await sm.FireAsync("Initialize");

            Assert.NotNull(received);
            Assert.Equal(DeviceState.Idle, received!.Value.From);
            Assert.Equal(DeviceState.Init, received.Value.To);
            Assert.Equal("Initialize", received.Value.Trigger);
        }

        // =============================================
        // 无效状态转换测试
        // =============================================

        /// <summary>
        /// 验证 Idle 状态下直接执行 "StartAuto" 应失败。
        /// 设备必须先经过 Init 状态才能进入 Auto。
        /// </summary>
        [Fact]
        public async Task StateMachine_IdleToAuto_ShouldFail()
        {
            var sm = CreateStateMachine();

            var result = await sm.FireAsync("StartAuto");

            Assert.False(result);
            Assert.Equal(DeviceState.Idle, sm.CurrentState); // 状态不变
        }

        /// <summary>
        /// 验证 Manual 状态下执行 "StartAuto" 应失败。
        /// 手动模式不能直接切换到自动模式。
        /// </summary>
        [Fact]
        public async Task StateMachine_ManualToAuto_ShouldFail()
        {
            var sm = CreateStateMachine();
            await sm.FireAsync("Initialize");
            await sm.FireAsync("StartManual");

            var result = await sm.FireAsync("StartAuto");

            Assert.False(result);
            Assert.Equal(DeviceState.Manual, sm.CurrentState);
        }

        /// <summary>
        /// 验证 Alarm 状态下有活跃报警时，Reset 应失败（守卫条件不满足）。
        /// 必须先清除所有报警才能复位。
        /// </summary>
        [Fact]
        public async Task StateMachine_AlarmReset_WithActiveAlarms_ShouldFail()
        {
            // 设置有活跃报警
            _mockAlarmService.Setup(a => a.ActiveAlarms)
                .Returns(new List<AlarmRecord>
                {
                    new AlarmRecord { AlarmCode = 1001, Level = AlarmLevel.Heavy }
                }.AsReadOnly());

            var sm = CreateStateMachine();
            // 使用 ForceState 直接进入 Alarm 状态（绕过守卫）
            sm.ForceState(DeviceState.Alarm);

            var result = await sm.FireAsync("Reset");

            Assert.False(result);
            Assert.Equal(DeviceState.Alarm, sm.CurrentState);
        }

        /// <summary>
        /// 验证 Idle → Init 在有活跃报警时应失败（守卫条件）。
        /// 有报警时不允许初始化设备。
        /// </summary>
        [Fact]
        public async Task StateMachine_IdleToInit_WithActiveAlarms_ShouldFail()
        {
            _mockAlarmService.Setup(a => a.ActiveAlarms)
                .Returns(new List<AlarmRecord>
                {
                    new AlarmRecord { AlarmCode = 1001 }
                }.AsReadOnly());

            var sm = CreateStateMachine();

            var result = await sm.FireAsync("Initialize");

            Assert.False(result);
            Assert.Equal(DeviceState.Idle, sm.CurrentState);
        }

        /// <summary>
        /// 验证发送未知的触发命令应失败。
        /// </summary>
        [Fact]
        public async Task StateMachine_UnknownTrigger_ShouldFail()
        {
            var sm = CreateStateMachine();

            var result = await sm.FireAsync("UnknownCommand");

            Assert.False(result);
            Assert.Equal(DeviceState.Idle, sm.CurrentState);
        }

        /// <summary>
        /// 验证无效转换后，不应发布 DeviceStateChangedEvent。
        /// </summary>
        [Fact]
        public async Task StateMachine_InvalidTransition_ShouldNotPublishEvent()
        {
            var sm = CreateStateMachine();

            await sm.FireAsync("StartAuto"); // 无效转换

            _mockEventBus.Verify(
                eb => eb.Publish(It.IsAny<DeviceStateChangedEvent>()),
                Times.Never);
        }

        /// <summary>
        /// 验证完整的生命周期流程：
        /// Idle → Init → Auto → Paused → Auto → Stop → Idle
        /// </summary>
        [Fact]
        public async Task StateMachine_FullLifecycle_ShouldTransitionCorrectly()
        {
            var sm = CreateStateMachine();

            // Idle → Init
            Assert.True(await sm.FireAsync("Initialize"));
            Assert.Equal(DeviceState.Init, sm.CurrentState);

            // Init → Auto
            Assert.True(await sm.FireAsync("StartAuto"));
            Assert.Equal(DeviceState.Auto, sm.CurrentState);

            // Auto → Paused
            Assert.True(await sm.FireAsync("Pause"));
            Assert.Equal(DeviceState.Paused, sm.CurrentState);

            // Paused → Auto
            Assert.True(await sm.FireAsync("Resume"));
            Assert.Equal(DeviceState.Auto, sm.CurrentState);

            // Auto → Idle
            Assert.True(await sm.FireAsync("Stop"));
            Assert.Equal(DeviceState.Idle, sm.CurrentState);
        }

        /// <summary>
        /// 使用 Theory 测试：从 Idle 状态尝试各种无效转换。
        /// </summary>
        [Theory]
        [InlineData("StartAuto")]
        [InlineData("Pause")]
        [InlineData("Resume")]
        [InlineData("Reset")]
        [InlineData("Alarm")]
        [InlineData("Stop")]
        public async Task StateMachine_IdleState_InvalidTriggers_ShouldFail(string trigger)
        {
            var sm = CreateStateMachine();

            var result = await sm.FireAsync(trigger);

            Assert.False(result);
            Assert.Equal(DeviceState.Idle, sm.CurrentState);
        }
    }
}
