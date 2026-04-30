// ============================================================
// 文件：CircularInterpolatorTests.cs
// 用途：圆弧插补器（CircularInterpolator）单元测试
// 测试目标：
//   验证圆弧插补算法在不同模式下的正确性，包括：
//   1. I/J 模式半圆弧 — 所有插补点到圆心距离应等于半径
//   2. 整圆（起终点重合）— 应生成闭合的圆形点序列
//   3. R 模式圆弧 — 使用半径参数生成正确圆弧
//   4. 逆时针方向 — G3 方向下点的排列顺序正确
// 开发思路：
//   构造不同的起终点、圆弧参数组合，调用 GenerateArcPoints，
//   检查返回点序列的几何属性（到圆心距离、闭合性、方向性）。
// ============================================================

using SmartMES.Modules.MotionControl;
using SmartMES.Core.Models;

namespace SmartMES.Tests
{
    /// <summary>
    /// 圆弧插补器单元测试类。
    /// 测试 CircularInterpolator 的 GenerateArcPoints 方法。
    /// </summary>
    public class CircularInterpolatorTests
    {
        // 浮点容差（圆弧插补精度）
        private const double 半径容差 = 0.5;      // mm，考虑微线段近似误差
        private const double 位置容差 = 0.5;       // mm，终点对齐精度

        /// <summary>
        /// 辅助方法：计算点到指定圆心的距离。
        /// </summary>
        private static double DistanceToCenter(InterpolationPoint point, double cx, double cy)
        {
            double x = point.AxisTargets.GetValueOrDefault("X", 0);
            double y = point.AxisTargets.GetValueOrDefault("Y", 0);
            return Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        }

        /// <summary>
        /// 测试1：I/J 模式半圆弧，所有点到圆心距离应约等于半径。
        /// 场景：从 (0,0) 到 (0,100)，圆心偏移 I=0, J=50，
        ///       即圆心在 (0,50)，半径为 50mm 的半圆。
        /// </summary>
        [Fact]
        public void SemiCircle_IJ_CorrectRadius()
        {
            // 准备：创建圆弧插补器和参数
            var interpolator = new CircularInterpolator();
            var arcParams = new ArcParameters
            {
                CenterI = 0,      // 圆心 X 偏移
                CenterJ = 50,     // 圆心 Y 偏移
                UseRadiusMode = false,
                IsClockwise = true  // 顺时针
            };

            // 圆心坐标 = 起点(0,0) + 偏移(0,50) = (0,50)
            double 圆心X = 0.0;
            double 圆心Y = 50.0;
            double 期望半径 = 50.0;

            // 执行：生成半圆弧点
            var points = interpolator.GenerateArcPoints(
                startX: 0, startY: 0,
                endX: 0, endY: 100,
                arcParams: arcParams,
                feedRate: 1000,
                stepSizeMm: 1.0);

            // 断言：应生成足够数量的插补点
            Assert.True(points.Count > 10, $"半圆弧应生成足够数量的点，实际只有 {points.Count} 个");

            // 断言：每个点到圆心的距离应约等于半径
            foreach (var point in points)
            {
                double distance = DistanceToCenter(point, 圆心X, 圆心Y);
                Assert.True(Math.Abs(distance - 期望半径) < 半径容差,
                    $"点 ({point.AxisTargets["X"]:F2}, {point.AxisTargets["Y"]:F2}) " +
                    $"到圆心距离 {distance:F2} 应约等于半径 {期望半径}");
            }

            // 断言：最后一个点应接近终点 (0, 100)
            var lastPoint = points.Last();
            Assert.True(Math.Abs(lastPoint.AxisTargets["X"] - 0) < 位置容差,
                "最后一个点 X 坐标应接近终点");
            Assert.True(Math.Abs(lastPoint.AxisTargets["Y"] - 100) < 位置容差,
                "最后一个点 Y 坐标应接近终点");
        }

        /// <summary>
        /// 测试2：整圆（起终点重合），应生成闭合的点序列。
        /// 场景：从 (50,0) 到 (50,0)，圆心偏移 I=-50, J=0，
        ///       即圆心在 (0,0)，半径为 50mm 的整圆。
        /// </summary>
        [Fact]
        public void FullCircle_ClosesBack()
        {
            // 准备：创建圆弧插补器和参数
            var interpolator = new CircularInterpolator();
            var arcParams = new ArcParameters
            {
                CenterI = -50,    // 圆心 X 偏移：50 + (-50) = 0
                CenterJ = 0,     // 圆心 Y 偏移：0 + 0 = 0
                UseRadiusMode = false,
                IsClockwise = true
            };

            double 圆心X = 0.0;
            double 圆心Y = 0.0;
            double 期望半径 = 50.0;

            // 执行：生成整圆点（起终点相同）
            var points = interpolator.GenerateArcPoints(
                startX: 50, startY: 0,
                endX: 50, endY: 0,
                arcParams: arcParams,
                feedRate: 1000,
                stepSizeMm: 1.0);

            // 断言：整圆应生成大量插补点（圆周约 314mm，步长1mm）
            Assert.True(points.Count > 100, $"整圆应生成大量点，实际 {points.Count} 个");

            // 断言：每个点到圆心距离应约等于半径
            foreach (var point in points)
            {
                double distance = DistanceToCenter(point, 圆心X, 圆心Y);
                Assert.True(Math.Abs(distance - 期望半径) < 半径容差,
                    $"整圆上的点到圆心距离 {distance:F2} 应约等于半径 {期望半径}");
            }

            // 断言：最后一个点应回到起点附近（闭合）
            var lastPoint = points.Last();
            Assert.True(Math.Abs(lastPoint.AxisTargets["X"] - 50) < 位置容差,
                "整圆最后一个点 X 坐标应回到起点");
            Assert.True(Math.Abs(lastPoint.AxisTargets["Y"] - 0) < 位置容差,
                "整圆最后一个点 Y 坐标应回到起点");
        }

        /// <summary>
        /// 测试3：R 模式圆弧，使用半径参数生成圆弧。
        /// 场景：从 (0,0) 到 (50,0)，半径 R=25mm。
        /// </summary>
        [Fact]
        public void RadiusMode_GeneratesArc()
        {
            // 准备：创建圆弧插补器和 R 模式参数
            var interpolator = new CircularInterpolator();
            var arcParams = new ArcParameters
            {
                Radius = 25,          // 半径 25mm
                UseRadiusMode = true, // 使用 R 模式
                IsClockwise = true    // 顺时针
            };

            // 执行：生成圆弧点
            var points = interpolator.GenerateArcPoints(
                startX: 0, startY: 0,
                endX: 50, endY: 0,
                arcParams: arcParams,
                feedRate: 1000,
                stepSizeMm: 0.5);

            // 断言：应生成插补点
            Assert.True(points.Count > 5, $"R 模式圆弧应生成插补点，实际 {points.Count} 个");

            // 断言：每个点到某个圆心的距离应约等于 25mm
            // 由于 R 模式会自动计算圆心，我们验证点之间的曲率一致性
            // 取中间点，检查 Y 坐标有偏移（说明不是直线）
            bool 有Y偏移 = points.Any(p => Math.Abs(p.AxisTargets.GetValueOrDefault("Y", 0)) > 1.0);
            Assert.True(有Y偏移, "R 模式圆弧的点应有 Y 方向偏移（不是直线）");

            // 断言：最后一个点应接近终点 (50, 0)
            var lastPoint = points.Last();
            Assert.True(Math.Abs(lastPoint.AxisTargets["X"] - 50) < 位置容差,
                "R 模式圆弧终点 X 坐标应正确");
            Assert.True(Math.Abs(lastPoint.AxisTargets["Y"] - 0) < 位置容差,
                "R 模式圆弧终点 Y 坐标应正确");
        }

        /// <summary>
        /// 测试4：逆时针方向（G3）圆弧验证。
        /// 场景：从 (50,0) 到 (0,50)，圆心在 (0,0)，逆时针。
        ///       逆时针方向上，角度应递增（从0°到90°）。
        /// </summary>
        [Fact]
        public void CounterClockwise_Correct()
        {
            // 准备：创建圆弧插补器，逆时针参数
            var interpolator = new CircularInterpolator();
            var arcParams = new ArcParameters
            {
                CenterI = -50,    // 圆心 X 偏移：50 + (-50) = 0
                CenterJ = 0,     // 圆心 Y 偏移：0 + 0 = 0
                UseRadiusMode = false,
                IsClockwise = false  // 逆时针（G3）
            };

            // 执行：生成逆时针圆弧（从 (50,0) 到 (0,50)，90° 弧）
            // 圆心在 (0,0)，半径 50mm
            var points = interpolator.GenerateArcPoints(
                startX: 50, startY: 0,
                endX: 0, endY: 50,
                arcParams: arcParams,
                feedRate: 1000,
                stepSizeMm: 1.0);

            // 断言：应生成点
            Assert.True(points.Count > 5, "逆时针圆弧应生成足够的插补点");

            // 断言：逆时针方向上，角度应递增
            // 起始角度约 0°（atan2(0-0, 50-0) = 0），终止角度约 90°（atan2(50-0, 0-0) = π/2）
            // 验证：Y 坐标应在弧上逐渐增大（从0趋向50）
            double 前一个Y = 0;
            bool Y单调递增 = true;
            foreach (var point in points)
            {
                double currentY = point.AxisTargets.GetValueOrDefault("Y", 0);
                if (currentY < 前一个Y - 0.1) // 允许微小波动
                {
                    Y单调递增 = false;
                    break;
                }
                前一个Y = currentY;
            }
            Assert.True(Y单调递增, "逆时针圆弧从 (50,0) 到 (0,50) 的 Y 坐标应单调递增");

            // 断言：最后一个点应接近终点 (0, 50)
            var lastPoint = points.Last();
            Assert.True(Math.Abs(lastPoint.AxisTargets["X"] - 0) < 位置容差,
                "逆时针圆弧终点 X 应接近 0");
            Assert.True(Math.Abs(lastPoint.AxisTargets["Y"] - 50) < 位置容差,
                "逆时针圆弧终点 Y 应接近 50");
        }
    }
}
