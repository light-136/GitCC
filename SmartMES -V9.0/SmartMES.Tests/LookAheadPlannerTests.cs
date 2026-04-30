// ============================================================
// 文件：LookAheadPlannerTests.cs
// 用途：前瞻速度规划器（LookAheadPlanner）单元测试
// 测试目标：
//   验证三遍前瞻规划算法的核心行为，包括：
//   1. 直线路径 — 中间段应达到接近最大速度
//   2. 急转弯 — 拐角处应降低速度
//   3. 末段出口速度 — 最后一段的 ExitVelocity 应为 0
//   4. 单点路径 — 应返回包含 1 个元素的列表
// 开发思路：
//   构造不同几何形状的路径点序列，调用 LookAheadPlanner.Plan，
//   检查返回的 PlannedSegment 列表中的速度分布是否符合物理约束。
// ============================================================

using SmartMES.Modules.MotionControl;

namespace SmartMES.Tests
{
    /// <summary>
    /// 前瞻速度规划器单元测试类。
    /// 测试 LookAheadPlanner.Plan 静态方法。
    /// </summary>
    public class LookAheadPlannerTests
    {
        // 默认运动参数
        private const double 最大速度 = 100.0;      // mm/s
        private const double 最大加速度 = 1000.0;    // mm/s²
        private const double 拐角容差 = 0.05;        // 拐角速度下限比例

        /// <summary>
        /// 辅助方法：创建一个插补点。
        /// </summary>
        /// <param name="x">X 坐标</param>
        /// <param name="y">Y 坐标</param>
        /// <param name="feedRate">进给速度（mm/s）</param>
        /// <returns>插补点对象</returns>
        private static InterpolationPoint MakePoint(double x, double y, double feedRate = 100.0)
        {
            return new InterpolationPoint
            {
                AxisTargets = new Dictionary<string, double> { ["X"] = x, ["Y"] = y },
                FeedRate = feedRate
            };
        }

        /// <summary>
        /// 辅助方法：生成沿 X 轴的直线路径。
        /// </summary>
        /// <param name="pointCount">点的数量</param>
        /// <param name="spacing">相邻点间距（mm）</param>
        /// <returns>直线路径点列表</returns>
        private static List<InterpolationPoint> MakeStraightLine(int pointCount, double spacing)
        {
            var path = new List<InterpolationPoint>();
            for (int i = 0; i < pointCount; i++)
            {
                path.Add(MakePoint(i * spacing, 0));
            }
            return path;
        }

        /// <summary>
        /// 测试1：直线路径中间段应达到接近最大速度。
        /// 场景：沿 X 轴的等间距直线路径（无拐角），
        ///       规划后中间段的入口/出口速度应接近 maxVelocity。
        /// </summary>
        [Fact]
        public void StraightLine_FullSpeed()
        {
            // 准备：生成 20 个等间距直线点，间距 10mm
            var path = MakeStraightLine(20, 10.0);

            // 执行：前瞻规划
            var segments = LookAheadPlanner.Plan(path, 最大速度, 最大加速度, 拐角容差);

            // 断言：应返回与路径点数相同的段数
            Assert.Equal(path.Count, segments.Count);

            // 断言：中间段（排除起止各2段加减速区域）应有较高速度
            // 取中间区域的段检查
            int midStart = segments.Count / 3;
            int midEnd = segments.Count * 2 / 3;
            for (int i = midStart; i < midEnd; i++)
            {
                // 直线路径无拐角，中间段速度应接近最大速度
                Assert.True(segments[i].EntryVelocity > 最大速度 * 0.5,
                    $"段 {i} 的入口速度 {segments[i].EntryVelocity:F2} 应接近最大速度 {最大速度}");
            }
        }

        /// <summary>
        /// 测试2：90° 急转弯处应降低速度。
        /// 场景：路径为 L 型（先沿 X 轴，再沿 Y 轴），
        ///       在拐角处的速度应明显低于直线段的速度。
        /// </summary>
        [Fact]
        public void SharpCorner_SlowsDown()
        {
            // 准备：构造 L 型路径
            // 第一段：沿 X 轴从 (0,0) 到 (50,0)，5 个点
            // 拐角点：(50,0)
            // 第二段：沿 Y 轴从 (50,0) 到 (50,50)，5 个点
            var path = new List<InterpolationPoint>();
            for (int i = 0; i <= 5; i++)
                path.Add(MakePoint(i * 10.0, 0));       // X 方向
            for (int i = 1; i <= 5; i++)
                path.Add(MakePoint(50, i * 10.0));       // Y 方向（90° 转弯）

            // 执行：前瞻规划
            var segments = LookAheadPlanner.Plan(path, 最大速度, 最大加速度, 拐角容差);

            // 拐角点在索引 5 的位置 (50,0)
            // 该点的 EntryVelocity 就是从 X 方向进入拐角时的速度
            // 对于 90° 转弯，junction velocity = maxVelocity * cos(45°) ≈ 0.707 * maxVelocity
            int 拐角索引 = 5;

            // 断言：拐角处的入口速度应低于最大速度的 85%
            // （90° 转弯的 cos(45°) ≈ 0.707）
            double 拐角速度 = segments[拐角索引].EntryVelocity;
            Assert.True(拐角速度 < 最大速度 * 0.85,
                $"90° 拐角处入口速度 {拐角速度:F2} 应明显低于最大速度 {最大速度}");

            // 断言：拐角速度应大于零（不是完全停止）
            Assert.True(拐角速度 > 0,
                "拐角处速度应大于零（前瞻规划应保持一定通过速度）");
        }

        /// <summary>
        /// 测试3：最后一段的 ExitVelocity 应为 0。
        /// 运动路径的终点处应完全停止。
        /// </summary>
        [Fact]
        public void LastSegment_ExitZero()
        {
            // 准备：生成直线路径
            var path = MakeStraightLine(10, 10.0);

            // 执行：前瞻规划
            var segments = LookAheadPlanner.Plan(path, 最大速度, 最大加速度, 拐角容差);

            // 断言：最后一段的出口速度应为 0
            var lastSegment = segments.Last();
            Assert.True(Math.Abs(lastSegment.ExitVelocity) < 1e-6,
                $"最后一段的出口速度应为 0，实际为 {lastSegment.ExitVelocity:F6}");

            // 断言：第一段的入口速度也应为 0（从静止开始）
            var firstSegment = segments.First();
            Assert.True(Math.Abs(firstSegment.EntryVelocity) < 1e-6,
                $"第一段的入口速度应为 0，实际为 {firstSegment.EntryVelocity:F6}");
        }

        /// <summary>
        /// 测试4：单点路径应返回包含 1 个元素的列表。
        /// 当路径只有一个点时，无法构成运动段，
        /// 但应返回一个零距离的规划段。
        /// </summary>
        [Fact]
        public void SinglePoint_ReturnsSingle()
        {
            // 准备：只有一个点的路径
            var path = new List<InterpolationPoint>
            {
                MakePoint(10, 20)
            };

            // 执行：前瞻规划
            var segments = LookAheadPlanner.Plan(path, 最大速度, 最大加速度, 拐角容差);

            // 断言：应返回 1 个段
            Assert.Single(segments);

            // 断言：该段的距离应为 0
            Assert.True(Math.Abs(segments[0].Distance) < 1e-6,
                "单点路径的段距离应为 0");

            // 断言：入口和出口速度都应为 0
            Assert.True(Math.Abs(segments[0].EntryVelocity) < 1e-6,
                "单点路径的入口速度应为 0");
            Assert.True(Math.Abs(segments[0].ExitVelocity) < 1e-6,
                "单点路径的出口速度应为 0");
        }
    }
}
