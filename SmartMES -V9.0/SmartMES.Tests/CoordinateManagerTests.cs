// ============================================================
// 文件：CoordinateManagerTests.cs
// 用途：坐标系管理器（CoordinateManager）单元测试
// 测试目标：
//   验证坐标系变换的正确性，包括：
//   1. 无偏移时的恒等变换 — 机械→工件→机械应返回原值
//   2. 平移变换 — X/Y/Z 偏移应正确应用到两个方向
//   3. 旋转变换 — 90° 旋转应正确变换坐标
//   4. 多坐标系独立性 — G54 和 G55 的设置互不影响
// 开发思路：
//   对 CoordinateManager 的 SetWorkOffset、MachineToWork、
//   WorkToMachine 方法进行组合测试，验证正变换和逆变换的
//   数学正确性以及多坐标系之间的隔离性。
// ============================================================

using SmartMES.Modules.MotionControl;
using SmartMES.Core.Models;

namespace SmartMES.Tests
{
    /// <summary>
    /// 坐标系管理器单元测试类。
    /// 测试 CoordinateManager 的坐标变换功能。
    /// </summary>
    public class CoordinateManagerTests
    {
        // 浮点比较容差
        private const double 容差 = 1e-6;

        /// <summary>
        /// 辅助方法：断言两个浮点数在容差范围内相等。
        /// </summary>
        private static void AssertClose(double expected, double actual, string message = "")
        {
            Assert.True(Math.Abs(expected - actual) <= 容差,
                $"期望 {expected:F6}，实际 {actual:F6}，容差 {容差}。{message}");
        }

        /// <summary>
        /// 测试1：无偏移时的恒等变换（往返转换）。
        /// 当坐标系没有设置任何偏移和旋转时，
        /// 机械坐标→工件坐标→机械坐标应返回原始值。
        /// </summary>
        [Fact]
        public void RoundTrip_IdentityWithNoOffset()
        {
            // 准备：创建坐标管理器（默认所有坐标系零偏移）
            var manager = new CoordinateManager();

            // 测试坐标
            double 原始X = 123.456;
            double 原始Y = 789.012;
            double 原始Z = -45.678;

            // 执行：机械→工件→机械
            var (workX, workY, workZ) = manager.MachineToWork(
                原始X, 原始Y, 原始Z, CoordinateSystem.Work1);
            var (backX, backY, backZ) = manager.WorkToMachine(
                workX, workY, workZ, CoordinateSystem.Work1);

            // 断言：往返后应回到原始值
            AssertClose(原始X, backX, "X 坐标往返后应不变");
            AssertClose(原始Y, backY, "Y 坐标往返后应不变");
            AssertClose(原始Z, backZ, "Z 坐标往返后应不变");

            // 断言：无偏移时，工件坐标应等于机械坐标
            AssertClose(原始X, workX, "无偏移时工件 X 应等于机械 X");
            AssertClose(原始Y, workY, "无偏移时工件 Y 应等于机械 Y");
            AssertClose(原始Z, workZ, "无偏移时工件 Z 应等于机械 Z");
        }

        /// <summary>
        /// 测试2：平移偏移应正确应用。
        /// 设置 G54 偏移 X=100, Y=50, Z=10，
        /// 验证 MachineToWork 和 WorkToMachine 双向变换。
        /// </summary>
        [Fact]
        public void Translation_AppliedCorrectly()
        {
            // 准备：创建坐标管理器并设置 G54 偏移
            var manager = new CoordinateManager();
            manager.SetWorkOffset(CoordinateSystem.Work1, new CoordinateTransform
            {
                OffsetX = 100.0,   // X 方向偏移 100mm
                OffsetY = 50.0,    // Y 方向偏移 50mm
                OffsetZ = 10.0,    // Z 方向偏移 10mm
                RotationDeg = 0.0  // 无旋转
            });

            // 执行：机械坐标 (150, 80, 30) → 工件坐标
            // 公式：work = machine - offset（无旋转时）
            var (workX, workY, workZ) = manager.MachineToWork(
                150.0, 80.0, 30.0, CoordinateSystem.Work1);

            // 断言：工件坐标 = (150-100, 80-50, 30-10) = (50, 30, 20)
            AssertClose(50.0, workX, "MachineToWork: X 偏移应正确减去");
            AssertClose(30.0, workY, "MachineToWork: Y 偏移应正确减去");
            AssertClose(20.0, workZ, "MachineToWork: Z 偏移应正确减去");

            // 执行：工件坐标 (50, 30, 20) → 机械坐标（逆变换）
            // 公式：machine = work + offset（无旋转时）
            var (machX, machY, machZ) = manager.WorkToMachine(
                50.0, 30.0, 20.0, CoordinateSystem.Work1);

            // 断言：机械坐标 = (50+100, 30+50, 20+10) = (150, 80, 30)
            AssertClose(150.0, machX, "WorkToMachine: X 应正确加上偏移");
            AssertClose(80.0, machY, "WorkToMachine: Y 应正确加上偏移");
            AssertClose(30.0, machZ, "WorkToMachine: Z 应正确加上偏移");
        }

        /// <summary>
        /// 测试3：90° 旋转变换验证。
        /// 设置 RotationDeg = 90°，验证旋转矩阵的正确性。
        /// 旋转公式（MachineToWork，先平移后旋转）：
        ///   dx = mx - offsetX, dy = my - offsetY
        ///   wx = dx * cos(θ) + dy * sin(θ)
        ///   wy = -dx * sin(θ) + dy * cos(θ)
        /// </summary>
        [Fact]
        public void Rotation_AppliedCorrectly()
        {
            // 准备：创建坐标管理器，设置 90° 旋转（无平移）
            var manager = new CoordinateManager();
            manager.SetWorkOffset(CoordinateSystem.Work1, new CoordinateTransform
            {
                OffsetX = 0.0,
                OffsetY = 0.0,
                OffsetZ = 0.0,
                RotationDeg = 90.0  // 绕 Z 轴旋转 90°
            });

            // 执行：机械坐标 (10, 0, 0) → 工件坐标
            // θ = 90°, cos(90°) = 0, sin(90°) = 1
            // wx = 10*0 + 0*1 = 0
            // wy = -10*1 + 0*0 = -10
            var (workX, workY, workZ) = manager.MachineToWork(
                10.0, 0.0, 0.0, CoordinateSystem.Work1);

            AssertClose(0.0, workX, "90° 旋转后 X 应为 0");
            AssertClose(-10.0, workY, "90° 旋转后 Y 应为 -10");
            AssertClose(0.0, workZ, "Z 坐标不受 XY 平面旋转影响");

            // 执行：验证逆变换（工件→机械）
            // 逆旋转：mx = wx*cos(θ) - wy*sin(θ), my = wx*sin(θ) + wy*cos(θ)
            // mx = 0*0 - (-10)*1 = 10
            // my = 0*1 + (-10)*0 = 0
            var (machX, machY, machZ) = manager.WorkToMachine(
                workX, workY, workZ, CoordinateSystem.Work1);

            AssertClose(10.0, machX, "逆变换后 X 应恢复为 10");
            AssertClose(0.0, machY, "逆变换后 Y 应恢复为 0");
            AssertClose(0.0, machZ, "逆变换后 Z 应恢复为 0");

            // 额外验证：机械坐标 (0, 10, 0) → 工件坐标
            // wx = 0*0 + 10*1 = 10
            // wy = -0*1 + 10*0 = 0
            var (workX2, workY2, _) = manager.MachineToWork(
                0.0, 10.0, 0.0, CoordinateSystem.Work1);

            AssertClose(10.0, workX2, "机械 Y 轴方向在 90° 旋转后应映射到工件 X 轴");
            AssertClose(0.0, workY2, "机械 Y 轴方向在 90° 旋转后工件 Y 应为 0");
        }

        /// <summary>
        /// 测试4：多坐标系独立性验证。
        /// G54 和 G55 应各自维护独立的偏移参数，
        /// 设置一个不应影响另一个。
        /// </summary>
        [Fact]
        public void MultiCoordSys_Independent()
        {
            // 准备：创建坐标管理器
            var manager = new CoordinateManager();

            // 设置 G54 偏移
            manager.SetWorkOffset(CoordinateSystem.Work1, new CoordinateTransform
            {
                OffsetX = 100.0,
                OffsetY = 200.0,
                OffsetZ = 0.0,
                RotationDeg = 0.0
            });

            // 设置 G55 偏移（不同的值）
            manager.SetWorkOffset(CoordinateSystem.Work2, new CoordinateTransform
            {
                OffsetX = -50.0,
                OffsetY = -100.0,
                OffsetZ = 5.0,
                RotationDeg = 45.0
            });

            // 执行：使用 G54 变换
            var (w1X, w1Y, w1Z) = manager.MachineToWork(
                200.0, 300.0, 10.0, CoordinateSystem.Work1);

            // 断言 G54：work = (200-100, 300-200, 10-0) = (100, 100, 10)
            AssertClose(100.0, w1X, "G54 X 偏移应独立正确");
            AssertClose(100.0, w1Y, "G54 Y 偏移应独立正确");
            AssertClose(10.0, w1Z, "G54 Z 偏移应独立正确");

            // 执行：使用 G55 变换相同的机械坐标
            var (w2X, w2Y, w2Z) = manager.MachineToWork(
                200.0, 300.0, 10.0, CoordinateSystem.Work2);

            // G55 有 45° 旋转，结果应与 G54 不同
            // dx = 200-(-50) = 250, dy = 300-(-100) = 400
            // cos(45°) ≈ 0.7071, sin(45°) ≈ 0.7071
            // wx = 250*0.7071 + 400*0.7071 ≈ 459.619
            // wy = -250*0.7071 + 400*0.7071 ≈ 106.066
            double cos45 = Math.Cos(45.0 * Math.PI / 180.0);
            double sin45 = Math.Sin(45.0 * Math.PI / 180.0);
            double expectedW2X = 250.0 * cos45 + 400.0 * sin45;
            double expectedW2Y = -250.0 * sin45 + 400.0 * cos45;

            AssertClose(expectedW2X, w2X, "G55 X 变换应独立于 G54");
            AssertClose(expectedW2Y, w2Y, "G55 Y 变换应独立于 G54");
            AssertClose(5.0, w2Z, "G55 Z 偏移应独立正确 (10-5=5)");

            // 断言：G54 和 G55 的结果应不同
            Assert.True(Math.Abs(w1X - w2X) > 1.0,
                "G54 和 G55 使用不同偏移/旋转，结果应不同");

            // 额外验证：修改 G55 不应影响 G54
            manager.SetWorkOffset(CoordinateSystem.Work2, new CoordinateTransform
            {
                OffsetX = 999.0,
                OffsetY = 999.0,
                OffsetZ = 999.0,
                RotationDeg = 0.0
            });

            // 重新用 G54 变换，结果应不变
            var (w1X_again, w1Y_again, w1Z_again) = manager.MachineToWork(
                200.0, 300.0, 10.0, CoordinateSystem.Work1);

            AssertClose(100.0, w1X_again, "修改 G55 后，G54 X 结果应不变");
            AssertClose(100.0, w1Y_again, "修改 G55 后，G54 Y 结果应不变");
            AssertClose(10.0, w1Z_again, "修改 G55 后，G54 Z 结果应不变");
        }
    }
}
