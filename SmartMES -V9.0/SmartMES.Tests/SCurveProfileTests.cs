// ============================================================
// 文件：SCurveProfileTests.cs
// 用途：S曲线运动规划器（SCurveProfile）单元测试
// 测试目标：
//   验证 S 曲线 7 段式运动规划的核心行为，包括：
//   1. 零距离运动应立即完成
//   2. 总时间结束时位置应等于总距离
//   3. 速度连续性（相邻采样点之间不应出现跳变）
//   4. 短距离退化行为（去掉匀速段仍然能正确完成运动）
//   5. 长距离运动中间点速度应接近最大速度
// 开发思路：
//   通过构造不同参数组合的运动场景，调用 Calculate 和
//   GetTotalTime 方法，断言运动学状态的正确性。
//   浮点数比较采用容差断言（AssertClose 辅助方法）。
// ============================================================

using SmartMES.Modules.MotionControl;
using SmartMES.Core.Interfaces;

namespace SmartMES.Tests
{
    /// <summary>
    /// S曲线运动规划器单元测试类。
    /// 测试 SCurveProfile 的 Calculate 和 GetTotalTime 方法。
    /// </summary>
    public class SCurveProfileTests
    {
        // 默认测试参数
        private const double 默认最大速度 = 500.0;      // mm/s
        private const double 默认最大加速度 = 5000.0;    // mm/s²
        private const double 默认总距离 = 100.0;         // mm
        private const double 浮点容差 = 1e-3;            // 通用浮点容差

        /// <summary>
        /// 辅助方法：断言两个浮点数在指定容差范围内相等。
        /// </summary>
        /// <param name="expected">期望值</param>
        /// <param name="actual">实际值</param>
        /// <param name="tolerance">允许的绝对误差</param>
        /// <param name="message">断言失败时的提示信息</param>
        private static void AssertClose(double expected, double actual, double tolerance, string message = "")
        {
            Assert.True(Math.Abs(expected - actual) <= tolerance,
                $"期望 {expected}，实际 {actual}，容差 {tolerance}。{message}");
        }

        /// <summary>
        /// 测试1：零距离运动应立即返回完成状态。
        /// 当 totalDistance = 0 时，Calculate 应返回 IsComplete = true，
        /// 且位置、速度、加速度均为零。
        /// </summary>
        [Fact]
        public void ZeroDistance_ReturnsComplete()
        {
            // 准备：创建 S 曲线规划器
            var profile = new SCurveProfile();

            // 执行：计算零距离运动在 t=0 时的状态
            var state = profile.Calculate(
                elapsedSeconds: 0.0,
                totalDistance: 0.0,
                maxVelocity: 默认最大速度,
                maxAcceleration: 默认最大加速度);

            // 断言：运动应立即完成
            Assert.True(state.IsComplete, "零距离运动应立即标记为完成");
            AssertClose(0.0, state.Position, 浮点容差, "零距离运动位置应为0");
            AssertClose(0.0, state.Velocity, 浮点容差, "零距离运动速度应为0");
        }

        /// <summary>
        /// 测试2：在总时间结束时，位置应恰好等于总距离。
        /// 先通过 GetTotalTime 获取总运动时间，
        /// 然后在该时间点调用 Calculate，检查位置是否到达终点。
        /// </summary>
        [Fact]
        public void CompletionAtTotalTime()
        {
            // 准备：创建 S 曲线规划器
            var profile = new SCurveProfile();
            double totalDistance = 默认总距离;

            // 执行：获取总时间并在该时间点计算状态
            double totalTime = profile.GetTotalTime(totalDistance, 默认最大速度, 默认最大加速度);
            var state = profile.Calculate(totalTime, totalDistance, 默认最大速度, 默认最大加速度);

            // 断言：位置应等于总距离，且标记为完成
            Assert.True(state.IsComplete, "在总时间结束时运动应标记为完成");
            AssertClose(totalDistance, state.Position, 0.01, "在总时间结束时位置应等于总距离");
        }

        /// <summary>
        /// 测试3：速度连续性验证。
        /// 在整个运动过程中多次采样，相邻采样点之间的速度变化量
        /// 不应超过 maxAcceleration * dt * 2（考虑 Jerk 段的加速度变化，
        /// 放宽到2倍余量以避免误报）。
        /// </summary>
        [Fact]
        public void VelocityContinuity()
        {
            // 准备：创建 S 曲线规划器
            var profile = new SCurveProfile();
            double totalDistance = 默认总距离;
            double maxAccel = 默认最大加速度;

            // 获取总时间
            double totalTime = profile.GetTotalTime(totalDistance, 默认最大速度, maxAccel);
            Assert.True(totalTime > 0, "总时间应为正值");

            // 执行：在运动过程中采样 200 个点
            int 采样数量 = 200;
            double dt = totalTime / 采样数量;
            double 上一速度 = 0.0;

            for (int i = 0; i <= 采样数量; i++)
            {
                double t = dt * i;
                var state = profile.Calculate(t, totalDistance, 默认最大速度, maxAccel);

                if (i > 0 && !state.IsComplete)
                {
                    // 速度变化量 = |当前速度 - 上一速度|
                    double 速度变化 = Math.Abs(state.Velocity - 上一速度);
                    // 允许的最大变化量 = maxAccel * dt * 2（含余量）
                    double 最大允许变化 = maxAccel * dt * 2.0;

                    Assert.True(速度变化 <= 最大允许变化 + 1e-6,
                        $"时间 t={t:F4}s 处速度跳变过大：变化量={速度变化:F2}，允许={最大允许变化:F2}");
                }

                上一速度 = state.Velocity;
            }
        }

        /// <summary>
        /// 测试4：短距离退化测试。
        /// 当运动距离非常短（0.1mm）时，S 曲线应退化
        /// （去掉匀速段甚至匀加速段），但仍然能正确完成运动。
        /// </summary>
        [Fact]
        public void ShortDistance_Degrades()
        {
            // 准备：创建 S 曲线规划器，使用极短距离
            var profile = new SCurveProfile();
            double 短距离 = 0.1; // mm

            // 执行：获取总时间
            double totalTime = profile.GetTotalTime(短距离, 默认最大速度, 默认最大加速度);

            // 断言：总时间应为正值（运动可以完成）
            Assert.True(totalTime > 0, "短距离运动的总时间应为正值");

            // 在总时间结束时检查位置
            var stateAtEnd = profile.Calculate(totalTime, 短距离, 默认最大速度, 默认最大加速度);
            Assert.True(stateAtEnd.IsComplete, "短距离运动应能正确完成");
            AssertClose(短距离, stateAtEnd.Position, 0.01, "短距离运动终点位置应正确");

            // 在运动中间检查：速度应为正值
            var stateMid = profile.Calculate(totalTime * 0.5, 短距离, 默认最大速度, 默认最大加速度);
            Assert.True(stateMid.Velocity >= 0, "短距离运动中间速度应非负");
        }

        /// <summary>
        /// 测试5：长距离运动中间点速度应接近最大速度。
        /// 当运动距离足够长时，中间段应进入匀速阶段，
        /// 此时速度应接近 maxVelocity。
        /// </summary>
        [Fact]
        public void MidpointVelocity()
        {
            // 准备：使用较长距离确保有匀速段
            var profile = new SCurveProfile();
            double 长距离 = 1000.0;  // mm，足够长以产生匀速段
            double maxVel = 默认最大速度;

            // 执行：获取总时间，在中间点采样
            double totalTime = profile.GetTotalTime(长距离, maxVel, 默认最大加速度);
            var stateMid = profile.Calculate(totalTime * 0.5, 长距离, maxVel, 默认最大加速度);

            // 断言：中间点速度应接近最大速度（允许10%的偏差）
            Assert.True(stateMid.Velocity > maxVel * 0.9,
                $"长距离运动中间点速度 {stateMid.Velocity:F2} 应接近最大速度 {maxVel:F2}（至少达到90%）");
            Assert.False(stateMid.IsComplete, "中间点运动不应已完成");
        }
    }
}
