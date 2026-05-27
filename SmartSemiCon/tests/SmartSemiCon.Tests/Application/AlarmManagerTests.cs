// ============================================================
// 文件：AlarmManagerTests.cs
// 用途：报警管理器单元测试
// 设计思路：
//   报警系统是工业设备安全的最后防线。
//   测试覆盖：
//   1. 触发报警 — 验证报警记录的创建和事件发布
//   2. 清除报警 — 验证报警的正确清除和状态更新
//   3. 一键清除 — 验证批量清除功能
//   4. 重复报警 — 验证同一报警代码不重复触发
//
//   使用 Moq 模拟 IEventBus 和 ILogService，
//   但 AlarmManager 本身使用真实实例，测试其内部逻辑。
// ============================================================

using Moq;
using SmartSemiCon.Application.Alarm;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Events;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.Tests.Application
{
    /// <summary>
    /// 报警管理器测试类 — 验证 AlarmManager 的核心报警管理逻辑。
    /// </summary>
    public class AlarmManagerTests
    {
        // Mock 依赖
        private readonly Mock<IEventBus> _mockEventBus;
        private readonly Mock<ILogService> _mockLogService;
        private readonly AlarmManager _alarmManager;

        /// <summary>
        /// 构造函数 — 初始化测试夹具（每个测试方法都会创建新实例）。
        /// </summary>
        public AlarmManagerTests()
        {
            _mockEventBus = new Mock<IEventBus>();
            _mockLogService = new Mock<ILogService>();
            _alarmManager = new AlarmManager(_mockEventBus.Object, _mockLogService.Object);
        }

        // =============================================
        // 触发报警测试
        // =============================================

        /// <summary>
        /// 验证触发报警后，ActiveAlarms 列表应包含该报警。
        /// </summary>
        [Fact]
        public void TriggerAlarm_ShouldAddToActiveAlarms()
        {
            // Act — 触发一个运动控制报警
            _alarmManager.TriggerAlarm(
                alarmCode: 1001,
                level: AlarmLevel.Heavy,
                message: "X轴跟随误差过大",
                source: "运动控制");

            // Assert — 活跃报警列表应有1条
            Assert.Single(_alarmManager.ActiveAlarms);
            Assert.Equal(1001, _alarmManager.ActiveAlarms[0].AlarmCode);
            Assert.Equal("X轴跟随误差过大", _alarmManager.ActiveAlarms[0].Message);
            Assert.Equal("运动控制", _alarmManager.ActiveAlarms[0].Source);
            Assert.Equal(AlarmLevel.Heavy, _alarmManager.ActiveAlarms[0].Level);
        }

        /// <summary>
        /// 验证触发报警后，应通过事件总线发布 AlarmTriggeredEvent。
        /// </summary>
        [Fact]
        public void TriggerAlarm_ShouldPublishAlarmTriggeredEvent()
        {
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "测试报警", "测试");

            // 验证事件总线被调用，发布了 AlarmTriggeredEvent
            _mockEventBus.Verify(
                eb => eb.Publish(It.Is<AlarmTriggeredEvent>(
                    e => e.Alarm.AlarmCode == 1001)),
                Times.Once);
        }

        /// <summary>
        /// 验证触发报警后，应记录日志。
        /// 重故障及以上级别应使用 Error 日志级别。
        /// </summary>
        [Fact]
        public void TriggerAlarm_HeavyLevel_ShouldLogAsError()
        {
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "重故障报警", "测试");

            // 验证日志服务被调用，且日志级别为 Error
            _mockLogService.Verify(
                ls => ls.Log(
                    LogLevel.Error,
                    It.IsAny<string>(),
                    It.Is<string>(m => m.Contains("1001")),
                    It.IsAny<string?>()),
                Times.Once);
        }

        /// <summary>
        /// 验证触发轻故障（Warning级别以下）报警时，应使用 Warning 日志级别。
        /// </summary>
        [Fact]
        public void TriggerAlarm_WarningLevel_ShouldLogAsWarning()
        {
            _alarmManager.TriggerAlarm(2001, AlarmLevel.Warning, "警告报警", "测试");

            _mockLogService.Verify(
                ls => ls.Log(
                    LogLevel.Warning,
                    It.IsAny<string>(),
                    It.Is<string>(m => m.Contains("2001")),
                    It.IsAny<string?>()),
                Times.Once);
        }

        /// <summary>
        /// 验证触发报警后，AlarmTriggered 事件应被触发。
        /// </summary>
        [Fact]
        public void TriggerAlarm_ShouldRaiseAlarmTriggeredEvent()
        {
            AlarmRecord? receivedAlarm = null;
            _alarmManager.AlarmTriggered += (_, alarm) => receivedAlarm = alarm;

            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "测试报警", "测试");

            Assert.NotNull(receivedAlarm);
            Assert.Equal(1001, receivedAlarm!.AlarmCode);
        }

        // =============================================
        // 清除报警测试
        // =============================================

        /// <summary>
        /// 验证清除报警后，ActiveAlarms 中不再包含该报警。
        /// </summary>
        [Fact]
        public void ClearAlarm_ShouldRemoveFromActiveAlarms()
        {
            // 先触发报警
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "测试报警", "测试");
            Assert.Single(_alarmManager.ActiveAlarms);

            // 清除报警
            _alarmManager.ClearAlarm(1001, "操作员A");

            // 活跃报警列表应为空
            Assert.Empty(_alarmManager.ActiveAlarms);
        }

        /// <summary>
        /// 验证清除报警后，应通过事件总线发布 AlarmClearedEvent。
        /// </summary>
        [Fact]
        public void ClearAlarm_ShouldPublishAlarmClearedEvent()
        {
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "测试报警", "测试");
            _alarmManager.ClearAlarm(1001, "操作员A");

            _mockEventBus.Verify(
                eb => eb.Publish(It.Is<AlarmClearedEvent>(
                    e => e.AlarmCode == 1001 && e.ClearedBy == "操作员A")),
                Times.Once);
        }

        /// <summary>
        /// 验证清除不存在的报警码时，不应抛出异常。
        /// </summary>
        [Fact]
        public void ClearAlarm_NonExistingCode_ShouldNotThrow()
        {
            var exception = Record.Exception(() =>
            {
                _alarmManager.ClearAlarm(9999, "操作员A");
            });

            Assert.Null(exception);
        }

        /// <summary>
        /// 验证清除报警后，AlarmCleared 事件应被触发。
        /// </summary>
        [Fact]
        public void ClearAlarm_ShouldRaiseAlarmClearedEvent()
        {
            int? clearedCode = null;
            _alarmManager.AlarmCleared += (_, code) => clearedCode = code;

            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "测试报警", "测试");
            _alarmManager.ClearAlarm(1001, "操作员A");

            Assert.Equal(1001, clearedCode);
        }

        // =============================================
        // 一键清除所有报警测试
        // =============================================

        /// <summary>
        /// 验证一键清除所有报警后，ActiveAlarms 应为空。
        /// </summary>
        [Fact]
        public void ClearAllAlarms_ShouldRemoveAllActiveAlarms()
        {
            // 触发多个报警
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "报警1", "运动控制");
            _alarmManager.TriggerAlarm(2001, AlarmLevel.Warning, "报警2", "视觉系统");
            _alarmManager.TriggerAlarm(3001, AlarmLevel.Light, "报警3", "通讯");
            Assert.Equal(3, _alarmManager.ActiveAlarms.Count);

            // 一键清除
            _alarmManager.ClearAllAlarms("管理员");

            Assert.Empty(_alarmManager.ActiveAlarms);
        }

        /// <summary>
        /// 验证一键清除时，每个报警都应发布 AlarmClearedEvent。
        /// </summary>
        [Fact]
        public void ClearAllAlarms_ShouldPublishEventForEachAlarm()
        {
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "报警1", "测试");
            _alarmManager.TriggerAlarm(2001, AlarmLevel.Warning, "报警2", "测试");

            _alarmManager.ClearAllAlarms("管理员");

            // 每个报警都应发布一次 AlarmClearedEvent
            _mockEventBus.Verify(
                eb => eb.Publish(It.IsAny<AlarmClearedEvent>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// 验证无报警时调用一键清除不应抛出异常。
        /// </summary>
        [Fact]
        public void ClearAllAlarms_WhenNoAlarms_ShouldNotThrow()
        {
            var exception = Record.Exception(() =>
            {
                _alarmManager.ClearAllAlarms("操作员");
            });

            Assert.Null(exception);
        }

        // =============================================
        // 重复报警不触发测试
        // =============================================

        /// <summary>
        /// 验证同一报警代码重复触发时，只应添加一次到 ActiveAlarms。
        /// 防止重复报警导致报警面板被刷爆。
        /// </summary>
        [Fact]
        public void TriggerAlarm_DuplicateCode_ShouldNotAddTwice()
        {
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "第一次", "测试");
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "第二次", "测试");

            // 只应有1条报警
            Assert.Single(_alarmManager.ActiveAlarms);
            // 保留第一次的消息
            Assert.Equal("第一次", _alarmManager.ActiveAlarms[0].Message);
        }

        /// <summary>
        /// 验证同一报警代码重复触发时，事件总线只应被调用一次。
        /// </summary>
        [Fact]
        public void TriggerAlarm_DuplicateCode_ShouldPublishOnlyOnce()
        {
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "第一次", "测试");
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "第二次", "测试");

            // AlarmTriggeredEvent 只应发布一次
            _mockEventBus.Verify(
                eb => eb.Publish(It.IsAny<AlarmTriggeredEvent>()),
                Times.Once);
        }

        /// <summary>
        /// 验证不同报警代码可以同时存在。
        /// </summary>
        [Fact]
        public void TriggerAlarm_DifferentCodes_ShouldAllBeActive()
        {
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "运动报警", "运动控制");
            _alarmManager.TriggerAlarm(2001, AlarmLevel.Warning, "视觉报警", "视觉系统");
            _alarmManager.TriggerAlarm(3001, AlarmLevel.Light, "通讯报警", "通讯");

            Assert.Equal(3, _alarmManager.ActiveAlarms.Count);
        }

        /// <summary>
        /// 验证报警清除后再次触发同一代码的报警，应重新添加。
        /// </summary>
        [Fact]
        public void TriggerAlarm_AfterClear_ShouldAddAgain()
        {
            // 触发 → 清除 → 再触发
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "第一次", "测试");
            _alarmManager.ClearAlarm(1001, "操作员");
            _alarmManager.TriggerAlarm(1001, AlarmLevel.Heavy, "第二次", "测试");

            Assert.Single(_alarmManager.ActiveAlarms);
            Assert.Equal("第二次", _alarmManager.ActiveAlarms[0].Message);
        }

        /// <summary>
        /// 使用 Theory 测试不同报警级别的触发。
        /// </summary>
        [Theory]
        [InlineData(AlarmLevel.Info)]
        [InlineData(AlarmLevel.Warning)]
        [InlineData(AlarmLevel.Light)]
        [InlineData(AlarmLevel.Heavy)]
        [InlineData(AlarmLevel.Fatal)]
        public void TriggerAlarm_AllLevels_ShouldBeStored(AlarmLevel level)
        {
            _alarmManager.TriggerAlarm(1001, level, "测试报警", "测试");

            Assert.Single(_alarmManager.ActiveAlarms);
            Assert.Equal(level, _alarmManager.ActiveAlarms[0].Level);
        }
    }
}
