// ============================================================
// 文件：LookAheadPlanner.cs
// 用途：前瞻速度规划器 — 三遍算法优化连续路径拐角速度
// 设计思路：
//   在连续路径运动中，如果每段都从零加速到零减速，效率极低。
//   前瞻规划器分析后续路径段的几何关系，在拐角处计算安全
//   通过速度，使运动在路径拐角处保持平滑连续。
//   算法分三遍：
//     1. 前向遍历：计算每个拐角的最大安全速度
//     2. 后向遍历：确保每段能从出口速度减速回来
//     3. 再前向：确保每段能从入口速度加速上去
// ============================================================

using SmartMES.Modules.MotionControl;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 规划后的路径段 — 包含入口/出口速度信息。
    /// </summary>
    public class PlannedSegment
    {
        /// <summary>目标插补点。</summary>
        public InterpolationPoint Target { get; set; } = new();

        /// <summary>进入该段的速度（mm/s）。</summary>
        public double EntryVelocity { get; set; }

        /// <summary>离开该段的速度（mm/s）。</summary>
        public double ExitVelocity { get; set; }

        /// <summary>该段的路径长度（mm）。</summary>
        public double Distance { get; set; }
    }

    /// <summary>
    /// 前瞻速度规划器 — 分析连续路径，优化拐角速度。
    /// </summary>
    public static class LookAheadPlanner
    {
        /// <summary>
        /// 对路径序列执行三遍前瞻规划。
        /// </summary>
        /// <param name="path">路径点序列。</param>
        /// <param name="maxVelocity">最大运动速度（mm/s）。</param>
        /// <param name="maxAcceleration">最大加速度（mm/s²）。</param>
        /// <param name="cornerTolerance">拐角速度下限比例（0~1，默认0.05=最大速度的5%）。</param>
        /// <returns>规划后的路径段列表。</returns>
        public static List<PlannedSegment> Plan(
            List<InterpolationPoint> path,
            double maxVelocity,
            double maxAcceleration,
            double cornerTolerance = 0.05)
        {
            if (path.Count < 2)
                return path.Select(p => new PlannedSegment
                {
                    Target = p, Distance = 0,
                    EntryVelocity = 0, ExitVelocity = 0
                }).ToList();

            // 构建段列表，计算每段距离和方向向量
            var segments = new List<PlannedSegment>();
            var directions = new List<(double dx, double dy)>();

            for (int i = 0; i < path.Count; i++)
            {
                var seg = new PlannedSegment { Target = path[i] };

                if (i > 0)
                {
                    // 计算与前一点之间的距离
                    double dx = 0, dy = 0;
                    var prev = path[i - 1];
                    var curr = path[i];

                    foreach (var axis in curr.AxisTargets.Keys)
                    {
                        double p = prev.AxisTargets.GetValueOrDefault(axis);
                        double c = curr.AxisTargets[axis];
                        double diff = c - p;
                        if (axis == "X") dx = diff;
                        else if (axis == "Y") dy = diff;
                    }

                    seg.Distance = Math.Sqrt(dx * dx + dy * dy);
                    double len = seg.Distance > 0.001 ? seg.Distance : 1.0;
                    directions.Add((dx / len, dy / len));
                }

                segments.Add(seg);
            }

            // ========== 第一遍（前向）：计算拐角安全速度 ==========
            var junctionVelocities = new double[segments.Count];
            junctionVelocities[0] = 0; // 起点速度为零

            for (int i = 1; i < segments.Count - 1; i++)
            {
                if (i - 1 < directions.Count && i < directions.Count)
                {
                    var d1 = directions[i - 1];
                    var d2 = directions[i];

                    // 计算两段方向向量的夹角
                    double dot = d1.dx * d2.dx + d1.dy * d2.dy;
                    dot = Math.Clamp(dot, -1.0, 1.0);
                    double angle = Math.Acos(dot);

                    // 拐角速度 = 最大速度 × cos(角度/2)
                    double jv = maxVelocity * Math.Cos(angle / 2.0);
                    // 下限：最大速度的 cornerTolerance 倍
                    jv = Math.Max(jv, maxVelocity * cornerTolerance);
                    junctionVelocities[i] = jv;
                }
                else
                {
                    junctionVelocities[i] = maxVelocity * cornerTolerance;
                }
            }
            junctionVelocities[segments.Count - 1] = 0; // 终点速度为零

            // 设置初始入口/出口速度
            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].EntryVelocity = junctionVelocities[i];
                segments[i].ExitVelocity = i < segments.Count - 1
                    ? junctionVelocities[i + 1] : 0;
            }

            // ========== 第二遍（后向）：确保减速可达 ==========
            for (int i = segments.Count - 2; i >= 0; i--)
            {
                double dist = segments[i + 1].Distance;
                if (dist < 0.001) continue;

                // 从下一段入口速度反推当前段的最大出口速度
                double nextEntry = segments[i + 1].EntryVelocity;
                double maxExit = Math.Sqrt(nextEntry * nextEntry + 2 * maxAcceleration * dist);
                segments[i].ExitVelocity = Math.Min(segments[i].ExitVelocity, maxExit);
                segments[i + 1].EntryVelocity = segments[i].ExitVelocity;
            }

            // ========== 第三遍（前向）：确保加速可达 ==========
            segments[0].EntryVelocity = 0;
            for (int i = 1; i < segments.Count; i++)
            {
                double dist = segments[i].Distance;
                if (dist < 0.001) continue;

                double prevExit = segments[i - 1].ExitVelocity;
                double maxEntry = Math.Sqrt(prevExit * prevExit + 2 * maxAcceleration * dist);
                segments[i].EntryVelocity = Math.Min(segments[i].EntryVelocity, maxEntry);
                segments[i - 1].ExitVelocity = segments[i].EntryVelocity;
            }

            return segments;
        }
    }
}
