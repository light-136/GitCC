// ============================================================
// 文件：ThirtyAxisControllerTests.cs
// 用途：30轴工业级运动控制系统的综合单元测试
// 设计思路：
//   针对 ThirtyAxisController 及其子模块（AxisGroupManager、
//   CollisionDetector、PerformanceMonitor 等）编写 15 个测试用例，
//   覆盖初始化、轴组管理、运动控制、急停复位、性能监控、碰撞检测
//   等核心场景。所有测试均使用 xUnit 框架，测试类实现 IDisposable
//   以确保每次测试后正确释放控制器资源。
//
//   命名规范：TestCategory_Condition_ExpectedResult
//   （测试类别_条件_期望结果）
// ============================================================

using Xunit;
using SmartMES.Modules.MotionControl;
using SmartMES.Core.Models;
using SmartMES.Core.Interfaces;

namespace SmartMES.Tests
{
    /// <summary>
    /// 30轴控制器综合测试类。
    /// 每个测试方法创建全新的控制器实例，测试结束后自动释放资源。
    /// </summary>
    public class ThirtyAxisControllerTests : IDisposable
    {
        /// <summary>被测试的30轴控制器实例。</summary>
        private readonly ThirtyAxisController _controller;

        /// <summary>
        /// 构造函数 — 在每个测试方法执行前创建新的控制器实例。
        /// 使用默认配置，包含30个轴、6个轴组、6个通道。
        /// </summary>
        public ThirtyAxisControllerTests()
        {
            _controller = new ThirtyAxisController();
        }

        /// <summary>
        /// 释放控制器资源（停止碰撞监控、性能采样等后台线程）。
        /// </summary>
        public void Dispose() => _controller.Dispose();

        // ======================== 初始化测试 ========================

        /// <summary>
        /// 测试1：初始化后应创建30个轴控制器实例。
        /// 验证默认配置下控制器的 Axes 字典包含恰好30个条目。
        /// </summary>
        [Fact]
        public void Initialization_Creates30Axes_AxesCountEquals30()
        {
            // 断言：轴数量应为30
            Assert.Equal(30, _controller.Axes.Count);
        }

        /// <summary>
        /// 测试2：初始化后应创建6个轴组。
        /// 验证默认配置下的轴组名称包含所有6个预定义组：
        /// Gantry1、Gantry2、Robot、Conveyor、Spindle、Auxiliary。
        /// </summary>
        [Fact]
        public void Initialization_Creates6Groups_AllGroupNamesPresent()
        {
            // 获取所有轴组名称
            var groupNames = _controller.GetGroupNames();

            // 断言：应有6个轴组
            Assert.Equal(6, groupNames.Count);

            // 断言：每个预定义轴组名称都应存在
            Assert.Contains("Gantry1", groupNames);
            Assert.Contains("Gantry2", groupNames);
            Assert.Contains("Robot", groupNames);
            Assert.Contains("Conveyor", groupNames);
            Assert.Contains("Spindle", groupNames);
            Assert.Contains("Auxiliary", groupNames);
        }

        /// <summary>
        /// 测试3：初始化后应创建6个通道（通道0~5）。
        /// 验证 GetChannelStatus() 返回恰好6个通道条目。
        /// </summary>
        [Fact]
        public void Initialization_Creates6Channels_ChannelStatusHas6Entries()
        {
            // 获取通道状态
            var channelStatus = _controller.GetChannelStatus();

            // 断言：应有6个通道
            Assert.Equal(6, channelStatus.Count);
        }

        /// <summary>
        /// 测试4：轴配置中应包含所有三种轴类型（Linear、Rotary、Spindle）。
        /// 验证默认配置覆盖了直线轴、旋转轴和主轴三种类型。
        /// </summary>
        [Fact]
        public void AxisConfigs_ContainsAllTypes_LinearRotarySpindlePresent()
        {
            // 获取所有轴配置
            var configs = _controller.AxisConfigs;

            // 断言：包含直线轴类型
            Assert.Contains(configs, c => c.AxisType == AxisType.Linear);

            // 断言：包含旋转轴类型
            Assert.Contains(configs, c => c.AxisType == AxisType.Rotary);

            // 断言：包含主轴类型
            Assert.Contains(configs, c => c.AxisType == AxisType.Spindle);
        }

        // ======================== 轴组管理测试 ========================

        /// <summary>
        /// 测试5：获取"Robot"轴组的轴名称列表，应包含J1~J6共6个轴。
        /// 验证 GroupManager.GetGroupAxes 返回正确的轴名称集合。
        /// </summary>
        [Fact]
        public void GroupManager_GetGroupAxes_ReturnsCorrectAxesForRobot()
        {
            // 获取Robot组的轴名称列表
            var robotAxes = _controller.GroupManager.GetGroupAxes("Robot");

            // 断言：Robot组应有6个轴
            Assert.Equal(6, robotAxes.Count);

            // 断言：应包含J1到J6所有关节轴
            Assert.Contains("J1", robotAxes);
            Assert.Contains("J2", robotAxes);
            Assert.Contains("J3", robotAxes);
            Assert.Contains("J4", robotAxes);
            Assert.Contains("J5", robotAxes);
            Assert.Contains("J6", robotAxes);
        }

        /// <summary>
        /// 测试6：初始化后所有轴组的轴都应处于空闲（Idle）状态。
        /// 验证 GroupManager.AreAllAxesIdle 在初始化后对所有组返回 true。
        /// </summary>
        [Fact]
        public void GroupManager_AreAllAxesIdle_TrueByDefault()
        {
            // 获取所有轴组名称
            var groupNames = _controller.GetGroupNames();

            // 断言：每个轴组的所有轴都应为 Idle 状态
            foreach (var groupName in groupNames)
            {
                Assert.True(
                    _controller.GroupManager.AreAllAxesIdle(groupName),
                    $"轴组 '{groupName}' 中存在非空闲状态的轴");
            }
        }

        // ======================== 运动控制测试 ========================

        /// <summary>
        /// 测试7：将轴移动到软限位范围内的目标位置，应返回 true。
        /// X1轴的软限位为 [-500, 500]，移动到100应该成功。
        /// </summary>
        [Fact]
        public void MoveAxis_WithinSoftLimits_ReturnsTrue()
        {
            // 执行：将X1轴移动到100mm（在软限位范围内）
            bool result = _controller.MoveAxis("X1", 100);

            // 断言：运动指令应成功发出
            Assert.True(result);
        }

        /// <summary>
        /// 测试8：将轴移动到超出软限位的目标位置，应返回 false。
        /// X1轴的软正限位为500，移动到999应被拒绝。
        /// </summary>
        [Fact]
        public void MoveAxis_ExceedsSoftLimit_ReturnsFalse()
        {
            // 执行：将X1轴移动到999mm（超出软正限位500）
            bool result = _controller.MoveAxis("X1", 999);

            // 断言：运动指令应被拒绝
            Assert.False(result);
        }

        // ======================== 急停与复位测试 ========================

        /// <summary>
        /// 测试9：执行急停后，所有轴应恢复到空闲状态。
        /// 先对X1发起运动，然后执行急停，验证轴状态回到 Idle。
        /// </summary>
        [Fact]
        public void EmergencyStop_StopsAllAxes_AllAxesReturnToIdle()
        {
            // 准备：对X1轴发起运动
            _controller.MoveAxis("X1", 100);

            // 执行：全系统急停
            _controller.EmergencyStop();

            // 等待一小段时间让停止操作生效
            Thread.Sleep(50);

            // 断言：X1轴状态应为 Idle
            Assert.Equal(AxisState.Idle, _controller.Axes["X1"].State);
        }

        // ======================== 性能监控测试 ========================

        /// <summary>
        /// 测试10：对所有轴进行一次性能采样，应返回30个快照。
        /// 验证 PerformanceMonitor.SampleAllAxes 能够采集所有已注册轴的数据。
        /// </summary>
        [Fact]
        public void PerformanceMonitor_SampleAllAxes_Returns30Snapshots()
        {
            // 执行：对所有轴进行一次采样
            var snapshots = _controller.PerformanceMonitor.SampleAllAxes();

            // 断言：应返回30个快照（每轴一个）
            Assert.Equal(30, snapshots.Count);
        }

        /// <summary>
        /// 测试11：生成系统性能报告，报告中的总轴数应为30。
        /// 验证 GenerateReport 能正确统计已注册的轴数量。
        /// </summary>
        [Fact]
        public void PerformanceMonitor_GenerateReport_TotalAxesEquals30()
        {
            // 执行：生成系统性能报告（使用默认60秒窗口）
            var report = _controller.PerformanceMonitor.GenerateReport(60);

            // 断言：报告中的总轴数应为30
            Assert.Equal(30, report.TotalAxes);
        }

        // ======================== 碰撞检测测试 ========================

        /// <summary>
        /// 测试12：初始化时应已配置默认碰撞区域。
        /// 当所有轴位置为0时（X1和X2均在 [-100,100] 范围内），
        /// CheckAllZones 应检测到碰撞事件（因为两轴距离为0，小于安全距离20mm）。
        /// </summary>
        [Fact]
        public void CollisionDetector_DefaultZoneConfigured_DetectsCollisionAtOrigin()
        {
            // 执行：检查所有碰撞区域
            // X1=0 和 X2=0 时，两轴都在 [-100,100] 范围内，
            // 距离为0，小于最小安全距离20mm，应触发碰撞检测
            var collisions = _controller.CollisionDetector.CheckAllZones();

            // 断言：应检测到至少一个碰撞事件（Gantry_X_Collision 区域）
            Assert.NotEmpty(collisions);

            // 断言：碰撞事件应来自 "Gantry_X_Collision" 区域
            Assert.Contains(collisions, e => e.ZoneName == "Gantry_X_Collision");
        }

        // ======================== 状态查询测试 ========================

        /// <summary>
        /// 测试13：获取"Gantry1"轴组状态，应返回5个轴的状态信息。
        /// Gantry1组包含 X1、Y1、Z1、A1、B1 共5个轴。
        /// </summary>
        [Fact]
        public void GroupStatus_ReturnsCorrectAxes_Gantry1Has5Entries()
        {
            // 执行：获取Gantry1组的状态
            var status = _controller.GetGroupStatus("Gantry1");

            // 断言：应返回5个轴的状态
            Assert.Equal(5, status.Count);
        }

        /// <summary>
        /// 测试14：获取所有轴的当前位置，应返回30个条目。
        /// 验证 GetAllPositions 返回所有轴的位置信息。
        /// </summary>
        [Fact]
        public void GetAllPositions_Returns30Entries_AllAxesPresent()
        {
            // 执行：获取所有轴的位置
            var positions = _controller.GetAllPositions();

            // 断言：应返回30个位置条目
            Assert.Equal(30, positions.Count);
        }

        // ======================== 复位测试 ========================

        /// <summary>
        /// 测试15：将一个轴设置为错误状态，然后执行全系统复位，
        /// 验证该轴恢复到空闲状态。
        /// ResetAll 内部调用 MultiAxisController.ResetAll()，
        /// 会对所有轴先 Stop 再 Reset，使 Error 状态的轴回到 Idle。
        /// </summary>
        [Fact]
        public void ResetAll_ResetsAllAxesToIdle_ErrorAxisBecomesIdle()
        {
            // 准备：将X1轴设置为错误状态
            _controller.Axes["X1"].SetError("测试错误");

            // 验证前提条件：X1应处于Error状态
            Assert.Equal(AxisState.Error, _controller.Axes["X1"].State);

            // 执行：全系统复位
            _controller.ResetAll();

            // 断言：X1轴应恢复到 Idle 状态
            Assert.Equal(AxisState.Idle, _controller.Axes["X1"].State);
        }
    }
}
