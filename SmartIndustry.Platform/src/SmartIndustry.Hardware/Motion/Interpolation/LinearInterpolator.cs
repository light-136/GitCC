// ============================================================
// 文件：LinearInterpolator.cs
// 层级：硬件抽象层（Hardware Layer）> Motion > Interpolation
// 职责：多轴直线插补算法。
//       将多轴（2-4轴）的起点→终点直线运动分解为一系列
//       微小位移段（微段化），每个微段各轴同步执行，
//       实现真正的直线轨迹联动。
//
// 算法原理：
//   1. 计算直线的总长度（欧式距离）
//   2. 将总长度按微段长度均匀分割为 N 段
//   3. 每段各轴的位移 = 总位移 × (微段长度/总长度)
//   4. 简单速度前瞻：在路径末端提前减速（最后20%的段减速）
//
// 使用方式：
//   var interp = new LinearInterpolator();
//   var segments = interp.Calculate(
//       startPos: new[]{0.0, 0.0},
//       endPos: new[]{100.0, 50.0},
//       feedRate: 200.0);
//   // 逐段执行
//   foreach(var seg in segments) { ... }
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Hardware.Motion.Interpolation
{
    /// <summary>
    /// 直线插补微段 — 描述多轴同步运动的一个小步骤。
    /// </summary>
    public class LinearSegment
    {
        /// <summary>微段序号（从0开始）</summary>
        public int Index { get; set; }

        /// <summary>各轴在此微段的增量位移（mm），长度=轴数</summary>
        public double[] DeltaPositions { get; set; } = Array.Empty<double>();

        /// <summary>各轴在此微段的目标绝对位置（mm）</summary>
        public double[] TargetPositions { get; set; } = Array.Empty<double>();

        /// <summary>此微段的进给速度（mm/s，已经过前瞻调整）</summary>
        public double FeedRate { get; set; }

        /// <summary>此微段的合成路径长度（mm）</summary>
        public double SegmentLength { get; set; }

        /// <summary>累计路径长度（mm，从起点到此微段末尾）</summary>
        public double AccumulatedLength { get; set; }
    }

    /// <summary>
    /// 直线插补结果
    /// </summary>
    public class LinearInterpolationResult
    {
        /// <summary>插补参与轴数</summary>
        public int AxisCount { get; set; }

        /// <summary>微段总数</summary>
        public int SegmentCount { get; set; }

        /// <summary>总路径长度（mm）</summary>
        public double TotalLength { get; set; }

        /// <summary>规划总时间（s）</summary>
        public double TotalTime { get; set; }

        /// <summary>微段列表（按执行顺序）</summary>
        public List<LinearSegment> Segments { get; set; } = new();
    }

    /// <summary>
    /// 多轴直线插补器（2~4轴）。
    /// 将多轴从起点到终点的直线路径分解为等长微段序列，
    /// 每个微段可作为一个独立的多轴同步运动指令执行。
    ///
    /// 速度前瞻（简单版本）：
    ///   在路径末端（最后 LookAheadRatio 比例处）开始减速，
    ///   保证到达终点时速度接近0，避免过冲。
    /// </summary>
    public class LinearInterpolator
    {
        // ==================== 配置参数 ====================

        /// <summary>默认微段长度（mm，越小路径越精确，但计算量越大）</summary>
        public double DefaultSegmentLength { get; set; } = 0.1;

        /// <summary>速度前瞻减速比例（末端此比例的路径进行减速）</summary>
        public double LookAheadRatio { get; set; } = 0.2;

        /// <summary>末端最小速度比例（减速至该比例的进给速度后维持到终点）</summary>
        public double EndVelocityRatio { get; set; } = 0.1;

        // ==================== 公开方法 ====================

        /// <summary>
        /// 计算直线插补微段序列。
        /// </summary>
        /// <param name="startPositions">各轴起始位置（mm），长度2~4</param>
        /// <param name="endPositions">各轴终止位置（mm），长度与起点相同</param>
        /// <param name="feedRate">合成进给速度（mm/s）</param>
        /// <param name="segmentLength">微段长度（mm，null=使用默认值）</param>
        /// <returns>插补微段列表</returns>
        public LinearInterpolationResult Calculate(
            double[] startPositions,
            double[] endPositions,
            double feedRate,
            double? segmentLength = null)
        {
            // ---- 参数校验 ----
            if (startPositions == null || endPositions == null)
                throw new ArgumentNullException("起点或终点坐标为空");
            if (startPositions.Length != endPositions.Length)
                throw new ArgumentException("起点和终点坐标维度不一致");
            if (startPositions.Length < 2 || startPositions.Length > 4)
                throw new ArgumentException($"直线插补支持2~4轴，当前轴数：{startPositions.Length}");
            if (feedRate <= 0) throw new ArgumentException($"进给速度必须大于0：{feedRate}");

            int axisCount = startPositions.Length;
            double segLen = segmentLength ?? DefaultSegmentLength;

            // ---- 计算总路径长度（欧式距离）----
            double totalLength = 0;
            double[] totalDeltas = new double[axisCount];
            for (int i = 0; i < axisCount; i++)
            {
                totalDeltas[i] = endPositions[i] - startPositions[i];
                totalLength += totalDeltas[i] * totalDeltas[i];
            }
            totalLength = Math.Sqrt(totalLength);

            // 路径长度为0，无需运动
            if (totalLength < 1e-6)
            {
                return new LinearInterpolationResult
                {
                    AxisCount = axisCount,
                    SegmentCount = 0,
                    TotalLength = 0,
                    TotalTime = 0
                };
            }

            // ---- 计算各轴方向余弦（单位方向向量）----
            double[] dirCosines = new double[axisCount];
            for (int i = 0; i < axisCount; i++)
                dirCosines[i] = totalDeltas[i] / totalLength;

            // ---- 计算微段数量 ----
            int n = (int)Math.Ceiling(totalLength / segLen);
            double actualSegLen = totalLength / n; // 调整后的实际微段长度

            // ---- 速度前瞻：计算各微段的进给速度 ----
            // 前瞻起始位置（路径 1-LookAheadRatio 处开始减速）
            double lookAheadStart = totalLength * (1.0 - LookAheadRatio);
            double endVelocity = feedRate * EndVelocityRatio;

            // ---- 生成微段列表 ----
            var segments = new List<LinearSegment>(n);
            double accumulated = 0;
            double totalTime = 0;

            for (int i = 0; i < n; i++)
            {
                // 此微段的实际长度（最后一段可能不足 actualSegLen）
                double thisLen = (i < n - 1) ? actualSegLen
                    : totalLength - actualSegLen * (n - 1);

                accumulated += thisLen;

                // 速度前瞻调整
                double segFeedRate = feedRate;
                if (accumulated > lookAheadStart)
                {
                    // 在减速区，线性减速到 endVelocity
                    double ratio = (accumulated - lookAheadStart) / (totalLength * LookAheadRatio);
                    ratio = Math.Clamp(ratio, 0.0, 1.0);
                    segFeedRate = feedRate - (feedRate - endVelocity) * ratio;
                }

                // 各轴增量 = 方向余弦 × 微段长度
                var deltaPos = new double[axisCount];
                var targetPos = new double[axisCount];
                for (int j = 0; j < axisCount; j++)
                {
                    deltaPos[j] = dirCosines[j] * thisLen;
                    targetPos[j] = startPositions[j] + dirCosines[j] * accumulated;
                }

                totalTime += thisLen / segFeedRate;

                segments.Add(new LinearSegment
                {
                    Index = i,
                    DeltaPositions = deltaPos,
                    TargetPositions = targetPos,
                    FeedRate = segFeedRate,
                    SegmentLength = thisLen,
                    AccumulatedLength = accumulated
                });
            }

            return new LinearInterpolationResult
            {
                AxisCount = axisCount,
                SegmentCount = n,
                TotalLength = totalLength,
                TotalTime = totalTime,
                Segments = segments
            };
        }

        /// <summary>
        /// 计算直线的合成路径长度（欧式距离，静态辅助方法）
        /// </summary>
        public static double GetLength(double[] start, double[] end)
        {
            double sum = 0;
            for (int i = 0; i < start.Length; i++)
            {
                double d = end[i] - start[i];
                sum += d * d;
            }
            return Math.Sqrt(sum);
        }
    }
}
