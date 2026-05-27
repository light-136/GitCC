// ============================================================
// 文件：TrapezoidalProfile.cs
// 层级：硬件抽象层（Hardware Layer）> Motion > Profiles
// 职责：梯形速度规划算法。
//       输入运动参数（距离、最大速度、加速度、减速度），
//       输出时间序列（每个时刻的位置、速度、加速度），
//       可用于运动仿真可视化或前馈控制。
//
// 算法说明：
//   梯形速度曲线分为最多3段：
//     段1（加速段）：从0加速到峰值速度，时长 t_a = v_peak / a
//     段2（匀速段）：以峰值速度匀速运动，时长 t_c（可能为0）
//     段3（减速段）：从峰值速度减速到0，时长 t_d = v_peak / d
//
//   三角形速度曲线（距离不足以到达最大速度）：
//     峰值速度 v_peak = sqrt(2 * distance * a * d / (a + d))
//     t_a = v_peak / a，t_d = v_peak / d，t_c = 0
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Hardware.Motion.Profiles
{
    /// <summary>
    /// 速度规划分段描述 — 描述一段匀加速/匀速/匀减速运动。
    /// </summary>
    public class MotionSegment
    {
        /// <summary>段名称（Acceleration/Constant/Deceleration）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>段开始时刻（s，相对于运动开始）</summary>
        public double StartTime { get; set; }

        /// <summary>段结束时刻（s）</summary>
        public double EndTime { get; set; }

        /// <summary>段时长（s）</summary>
        public double Duration => EndTime - StartTime;

        /// <summary>段开始时的速度（mm/s）</summary>
        public double StartVelocity { get; set; }

        /// <summary>段结束时的速度（mm/s）</summary>
        public double EndVelocity { get; set; }

        /// <summary>本段加速度（mm/s²，减速段为负值）</summary>
        public double Acceleration { get; set; }

        /// <summary>本段位移（mm）</summary>
        public double Distance { get; set; }
    }

    /// <summary>
    /// 运动规划结果 — 包含全部时间段和时间序列采样点。
    /// </summary>
    public class MotionProfileResult
    {
        /// <summary>总运动时间（s）</summary>
        public double TotalTime { get; set; }

        /// <summary>总运动距离（mm）</summary>
        public double TotalDistance { get; set; }

        /// <summary>峰值速度（mm/s）</summary>
        public double PeakVelocity { get; set; }

        /// <summary>是否为三角形速度曲线（未到达最大速度）</summary>
        public bool IsTriangular { get; set; }

        /// <summary>运动段列表（加速段、匀速段、减速段）</summary>
        public List<MotionSegment> Segments { get; set; } = new();

        /// <summary>时间序列采样点（时间间隔由采样周期决定）</summary>
        public List<MotionSample> Samples { get; set; } = new();
    }

    /// <summary>
    /// 运动状态采样点 — 描述某一时刻的完整运动学状态。
    /// </summary>
    public class MotionSample
    {
        /// <summary>时刻（s，相对于运动开始）</summary>
        public double Time { get; set; }

        /// <summary>位置（mm，相对于起点）</summary>
        public double Position { get; set; }

        /// <summary>速度（mm/s）</summary>
        public double Velocity { get; set; }

        /// <summary>加速度（mm/s²）</summary>
        public double Acceleration { get; set; }
    }

    /// <summary>
    /// 梯形速度规划器。
    /// 给定运动参数，计算完整的梯形（或三角形）速度曲线，
    /// 并生成高密度的时间序列采样点供仿真和分析使用。
    ///
    /// 使用示例：
    ///   var profile = new TrapezoidalProfile();
    ///   var result = profile.Calculate(
    ///       distance: 100.0,
    ///       maxVelocity: 200.0,
    ///       acceleration: 500.0,
    ///       deceleration: 500.0,
    ///       samplePeriodMs: 10);
    ///   Console.WriteLine($"总时间：{result.TotalTime:F3}s");
    /// </summary>
    public class TrapezoidalProfile
    {
        // ==================== 公开方法 ====================

        /// <summary>
        /// 计算梯形速度曲线。
        /// </summary>
        /// <param name="distance">总运动距离（mm，必须 &gt; 0）</param>
        /// <param name="maxVelocity">最大速度（mm/s，必须 &gt; 0）</param>
        /// <param name="acceleration">加速度（mm/s²，必须 &gt; 0）</param>
        /// <param name="deceleration">减速度（mm/s²，必须 &gt; 0，通常与加速度相同）</param>
        /// <param name="samplePeriodMs">采样周期（ms，决定 Samples 的密度）</param>
        /// <returns>完整的运动规划结果</returns>
        public MotionProfileResult Calculate(
            double distance,
            double maxVelocity,
            double acceleration,
            double deceleration,
            double samplePeriodMs = 10.0)
        {
            // ---- 参数校验 ----
            if (distance <= 0) throw new ArgumentException($"距离必须大于0，当前值：{distance}");
            if (maxVelocity <= 0) throw new ArgumentException($"最大速度必须大于0，当前值：{maxVelocity}");
            if (acceleration <= 0) throw new ArgumentException($"加速度必须大于0，当前值：{acceleration}");
            if (deceleration <= 0) throw new ArgumentException($"减速度必须大于0，当前值：{deceleration}");

            // ---- 计算峰值速度（判断梯形还是三角形）----
            // 加速到最大速度需要的距离：d_a = v_max² / (2*a)
            // 从最大速度减速到0需要的距离：d_d = v_max² / (2*d)
            double distAccelFull = (maxVelocity * maxVelocity) / (2 * acceleration);
            double distDecelFull = (maxVelocity * maxVelocity) / (2 * deceleration);

            double peakVelocity;
            bool isTriangular = false;

            if (distAccelFull + distDecelFull >= distance)
            {
                // 三角形速度曲线：实际能达到的峰值速度
                // v_peak = sqrt(2 * distance * a * d / (a + d))
                peakVelocity = Math.Sqrt(2.0 * distance * acceleration * deceleration
                                         / (acceleration + deceleration));
                isTriangular = true;
            }
            else
            {
                // 梯形速度曲线：可以达到最大速度
                peakVelocity = maxVelocity;
            }

            // ---- 计算各段时间 ----
            double ta = peakVelocity / acceleration;               // 加速时长（s）
            double td = peakVelocity / deceleration;               // 减速时长（s）
            double sa = 0.5 * acceleration * ta * ta;              // 加速段位移（mm）
            double sd = 0.5 * deceleration * td * td;              // 减速段位移（mm）
            double sc = distance - sa - sd;                        // 匀速段位移（mm）
            double tc = isTriangular ? 0 : sc / peakVelocity;     // 匀速时长（s）
            double totalTime = ta + tc + td;

            // ---- 构建分段列表 ----
            var segments = new List<MotionSegment>();

            // 加速段
            segments.Add(new MotionSegment
            {
                Name = "加速段",
                StartTime = 0,
                EndTime = ta,
                StartVelocity = 0,
                EndVelocity = peakVelocity,
                Acceleration = acceleration,
                Distance = sa
            });

            // 匀速段（三角形曲线时长为0，但仍加入列表保持结构一致）
            segments.Add(new MotionSegment
            {
                Name = "匀速段",
                StartTime = ta,
                EndTime = ta + tc,
                StartVelocity = peakVelocity,
                EndVelocity = peakVelocity,
                Acceleration = 0,
                Distance = sc
            });

            // 减速段
            segments.Add(new MotionSegment
            {
                Name = "减速段",
                StartTime = ta + tc,
                EndTime = totalTime,
                StartVelocity = peakVelocity,
                EndVelocity = 0,
                Acceleration = -deceleration,
                Distance = sd
            });

            // ---- 生成时间序列采样点 ----
            double dt = samplePeriodMs / 1000.0; // 转换为秒
            var samples = new List<MotionSample>();
            int sampleCount = (int)Math.Ceiling(totalTime / dt) + 1;

            for (int i = 0; i <= sampleCount; i++)
            {
                double t = Math.Min(i * dt, totalTime);
                var (pos, vel, acc) = GetStateAtTime(t, ta, tc, td, peakVelocity, acceleration, deceleration, distance);
                samples.Add(new MotionSample
                {
                    Time = t,
                    Position = pos,
                    Velocity = vel,
                    Acceleration = acc
                });

                if (t >= totalTime) break;
            }

            return new MotionProfileResult
            {
                TotalTime = totalTime,
                TotalDistance = distance,
                PeakVelocity = peakVelocity,
                IsTriangular = isTriangular,
                Segments = segments,
                Samples = samples
            };
        }

        /// <summary>
        /// 计算指定时刻的运动状态（不生成完整采样点，用于实时控制）。
        /// </summary>
        /// <param name="elapsedSeconds">从运动开始经过的时间（s）</param>
        /// <param name="distance">总距离（mm）</param>
        /// <param name="maxVelocity">最大速度（mm/s）</param>
        /// <param name="acceleration">加速度（mm/s²）</param>
        /// <param name="deceleration">减速度（mm/s²）</param>
        /// <returns>(位置, 速度, 加速度, 是否完成)</returns>
        public (double Position, double Velocity, double Acceleration, bool IsComplete)
            GetStateAtElapsed(double elapsedSeconds, double distance, double maxVelocity,
                double acceleration, double deceleration)
        {
            var result = Calculate(distance, maxVelocity, acceleration, deceleration, 100);
            if (elapsedSeconds >= result.TotalTime)
                return (distance, 0, 0, true);

            var (pos, vel, acc) = GetStateAtTime(elapsedSeconds,
                result.Segments[0].Duration,
                result.Segments[1].Duration,
                result.Segments[2].Duration,
                result.PeakVelocity,
                acceleration, deceleration, distance);

            return (pos, vel, acc, false);
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 根据时刻 t 计算梯形曲线的位置/速度/加速度
        /// </summary>
        private static (double pos, double vel, double acc) GetStateAtTime(
            double t, double ta, double tc, double td,
            double peakV, double accel, double decel, double totalDist)
        {
            if (t <= 0) return (0, 0, 0);

            double pos, vel, acc;

            if (t <= ta)
            {
                // 加速段
                acc = accel;
                vel = accel * t;
                pos = 0.5 * accel * t * t;
            }
            else if (t <= ta + tc)
            {
                // 匀速段
                double sa = 0.5 * accel * ta * ta;
                double dt = t - ta;
                acc = 0;
                vel = peakV;
                pos = sa + peakV * dt;
            }
            else
            {
                // 减速段
                double sa = 0.5 * accel * ta * ta;
                double sc = peakV * tc;
                double dt = t - ta - tc;
                double dtClamped = Math.Min(dt, td);
                acc = -decel;
                vel = Math.Max(0, peakV - decel * dtClamped);
                pos = sa + sc + peakV * dtClamped - 0.5 * decel * dtClamped * dtClamped;
            }

            // 限制到总距离（数值精度保护）
            pos = Math.Min(pos, totalDist);
            return (pos, vel, acc);
        }
    }
}
