// ============================================================
// 文件：SCurveProfile.cs
// 层级：硬件抽象层（Hardware Layer）> Motion > Profiles
// 职责：S曲线（7段式）速度规划算法。
//       在梯形曲线基础上增加 Jerk（加加速度）限制，使速度曲线平滑，
//       消除加速度突变带来的冲击，适用于精密定位场景。
//
// 七段式 S 曲线定义：
//   段1 (t1)：加加速段  — Jerk = +J，加速度从0增加到 Amax
//   段2 (t2)：匀加速段  — Jerk = 0，加速度保持 Amax（可能为0，即纯S曲线）
//   段3 (t3)：减加速段  — Jerk = -J，加速度从 Amax 减少到0（速度到达峰值）
//   段4 (t4)：匀速段    — Jerk = 0，加速度 = 0，速度 = Vmax
//   段5 (t5)：加减速段  — Jerk = -J，加速度从0减少到 -Amax
//   段6 (t6)：匀减速段  — Jerk = 0，加速度保持 -Amax
//   段7 (t7)：减减速段  — Jerk = +J，加速度从 -Amax 增加到0（速度到0）
//
//   注：加减速过程对称，即 t1=t3=t5=t7=Amax/J，t2=t6
//
// 简化假设（实用场景）：
//   - 起始速度和终止速度均为0
//   - 加速过程与减速过程的 Amax 和 J 相同（对称S曲线）
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Hardware.Motion.Profiles
{
    /// <summary>
    /// S曲线速度规划器（七段式，Jerk限制）。
    /// 相比梯形曲线，S曲线在加减速段的速度变化更平滑，
    /// 大幅降低机械冲击，提升定位精度和使用寿命。
    ///
    /// 使用示例：
    ///   var profile = new SCurveProfile();
    ///   var result = profile.Calculate(
    ///       distance: 100.0,
    ///       maxVelocity: 200.0,
    ///       maxAcceleration: 500.0,
    ///       jerk: 5000.0);
    /// </summary>
    public class SCurveProfile
    {
        // ==================== 公开方法 ====================

        /// <summary>
        /// 计算S曲线速度规划。
        /// </summary>
        /// <param name="distance">总运动距离（mm）</param>
        /// <param name="maxVelocity">最大速度（mm/s）</param>
        /// <param name="maxAcceleration">最大加速度（mm/s²）</param>
        /// <param name="jerk">最大急动度 Jerk（mm/s³）</param>
        /// <param name="samplePeriodMs">采样周期（ms）</param>
        /// <returns>完整的运动规划结果（格式与 TrapezoidalProfile 相同）</returns>
        public MotionProfileResult Calculate(
            double distance,
            double maxVelocity,
            double maxAcceleration,
            double jerk,
            double samplePeriodMs = 10.0)
        {
            // ---- 参数校验 ----
            if (distance <= 0) throw new ArgumentException($"距离必须大于0：{distance}");
            if (maxVelocity <= 0) throw new ArgumentException($"最大速度必须大于0：{maxVelocity}");
            if (maxAcceleration <= 0) throw new ArgumentException($"最大加速度必须大于0：{maxAcceleration}");
            if (jerk <= 0) throw new ArgumentException($"Jerk必须大于0：{jerk}");

            // ---- 计算七段时间参数 ----
            // t_j1 = t_j2（Jerk段时长）= Amax / J
            // 若加速到 Vmax 的过程中 Amax 可达，则存在匀加速段 t2
            // 否则（短距离），实际加速度达不到 Amax
            double tj = maxAcceleration / jerk;  // 单个 Jerk 段时长（s）

            // 检查是否能达到最大加速度（速度增量 v_j1 = 0.5 * J * tj² × 2 = Amax * tj）
            // 加速完整S弧需要的速度增量：delta_v = Amax * tj
            // 若 Vmax < Amax * tj，则 Amax 不可达（纯S曲线，无匀加速段）
            double deltaV_fullAccel = maxAcceleration * tj; // 完整Jerk段对应的速度增量

            double actualAmax;   // 实际最大加速度
            double tj_actual;    // 实际 Jerk 段时长
            double t2;           // 匀加速段时长（段2=段6）

            if (maxVelocity < deltaV_fullAccel)
            {
                // 纯S曲线：加速度不能达到 Amax
                // 实际 Jerk 段：v = J * tj_act² => tj_act = sqrt(Vmax / J)
                tj_actual = Math.Sqrt(maxVelocity / jerk);
                actualAmax = jerk * tj_actual;
                t2 = 0;
            }
            else
            {
                tj_actual = tj;
                actualAmax = maxAcceleration;
                // 匀加速段：v = 2 * deltaV_j + Amax * t2 = Vmax
                // => t2 = (Vmax - 2 * Amax * tj) / Amax
                t2 = (maxVelocity - 2 * actualAmax * tj_actual) / actualAmax;
                t2 = Math.Max(0, t2);
            }

            // 加速阶段总时长（段1+2+3）：ta = 2 * tj + t2
            double ta = 2 * tj_actual + t2;

            // 加速阶段总距离：sa = Vmax * ta / 2（面积法，起末速度分别为0和Vmax）
            double sa = maxVelocity * ta / 2.0;

            // 由于曲线对称，减速阶段参数与加速相同
            double td = ta;
            double sd = sa;

            // 匀速段时长和距离
            double sc = distance - sa - sd;
            double tc;

            double peakVelocity = maxVelocity;
            bool isTriangular = false;

            if (sc < 0)
            {
                // 距离不足，达不到最大速度，需要降低峰值速度
                // 近似计算：sa ≈ Vmax * ta / 2 反推可用的 Vmax
                // 使用二分法精确求解
                peakVelocity = SolvePeakVelocity(distance, maxAcceleration, jerk);
                isTriangular = true;

                // 用降低后的峰值速度重新计算
                return CalculateWithPeakVelocity(distance, peakVelocity, maxAcceleration, jerk, samplePeriodMs, isTriangular);
            }

            tc = sc / maxVelocity;
            double totalTime = ta + tc + td;

            return BuildResult(distance, maxVelocity, totalTime, ta, tc, td,
                tj_actual, t2, actualAmax, jerk, isTriangular, samplePeriodMs);
        }

        /// <summary>
        /// 计算指定时刻的运动状态（实时控制使用）
        /// </summary>
        public (double Position, double Velocity, double Acceleration, double Jerk, bool IsComplete)
            GetStateAtElapsed(double elapsed, double distance, double maxVelocity,
                double maxAcceleration, double jerkValue)
        {
            var result = Calculate(distance, maxVelocity, maxAcceleration, jerkValue, 100.0);
            if (elapsed >= result.TotalTime) return (distance, 0, 0, 0, true);

            // 在采样点列表中插值
            var samples = result.Samples;
            for (int i = 0; i < samples.Count - 1; i++)
            {
                if (elapsed >= samples[i].Time && elapsed <= samples[i + 1].Time)
                {
                    double ratio = (elapsed - samples[i].Time) / (samples[i + 1].Time - samples[i].Time);
                    double pos = samples[i].Position + ratio * (samples[i + 1].Position - samples[i].Position);
                    double vel = samples[i].Velocity + ratio * (samples[i + 1].Velocity - samples[i].Velocity);
                    double acc = samples[i].Acceleration + ratio * (samples[i + 1].Acceleration - samples[i].Acceleration);
                    return (pos, vel, acc, 0, false);
                }
            }
            return (distance, 0, 0, 0, true);
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 用给定峰值速度计算S曲线参数（当距离不足时使用降低后的速度）
        /// </summary>
        private MotionProfileResult CalculateWithPeakVelocity(
            double distance, double peakVelocity,
            double maxAcceleration, double jerk,
            double samplePeriodMs, bool isTriangular)
        {
            double tj = jerk > 0 ? Math.Min(maxAcceleration / jerk, Math.Sqrt(peakVelocity / jerk)) : 0;
            double actualAmax = jerk * tj;
            double t2 = actualAmax > 0 ? Math.Max(0, (peakVelocity - 2 * actualAmax * tj) / actualAmax) : 0;
            double ta = 2 * tj + t2;
            double td = ta;
            double totalTime = ta + td; // tc=0（三角形）

            return BuildResult(distance, peakVelocity, totalTime, ta, 0, td,
                tj, t2, actualAmax, jerk, isTriangular, samplePeriodMs);
        }

        /// <summary>
        /// 构建最终的 MotionProfileResult，包含分段信息和采样点
        /// </summary>
        private MotionProfileResult BuildResult(
            double distance, double peakVelocity, double totalTime,
            double ta, double tc, double td,
            double tj, double t2, double actualAmax, double jerk,
            bool isTriangular, double samplePeriodMs)
        {
            // ---- 构建7段描述 ----
            var segments = new List<MotionSegment>();
            double t = 0;

            // 段1：加加速（Jerk=+J，加速度 0→Amax）
            AddSegment(segments, "加加速段", ref t, tj, 0, actualAmax, jerk);

            // 段2：匀加速（Jerk=0，加速度=Amax）- 可能为0
            AddSegment(segments, "匀加速段", ref t, t2, actualAmax, actualAmax, 0);

            // 段3：减加速（Jerk=-J，加速度 Amax→0）
            AddSegment(segments, "减加速段", ref t, tj, actualAmax, 0, -jerk);

            // 段4：匀速段（Jerk=0，加速度=0）- 三角形曲线时为0
            AddSegment(segments, "匀速段", ref t, tc, 0, 0, 0);

            // 段5：加减速（Jerk=-J，加速度 0→-Amax）
            AddSegment(segments, "加减速段", ref t, tj, 0, -actualAmax, -jerk);

            // 段6：匀减速（Jerk=0，加速度=-Amax）
            AddSegment(segments, "匀减速段", ref t, t2, -actualAmax, -actualAmax, 0);

            // 段7：减减速（Jerk=+J，加速度 -Amax→0）
            AddSegment(segments, "减减速段", ref t, tj, -actualAmax, 0, jerk);

            // ---- 生成时间序列采样点 ----
            double dt = samplePeriodMs / 1000.0;
            var samples = new List<MotionSample>();

            for (double time = 0; time <= totalTime + dt * 0.5; time += dt)
            {
                double clampedT = Math.Min(time, totalTime);
                var (pos, vel, acc) = IntegrateState(clampedT, totalTime, distance, peakVelocity,
                    ta, tc, td, tj, t2, actualAmax, jerk);
                samples.Add(new MotionSample
                {
                    Time = clampedT,
                    Position = pos,
                    Velocity = vel,
                    Acceleration = acc
                });
                if (time >= totalTime) break;
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
        /// 向段列表添加一个运动段（速度和位移通过积分计算）
        /// </summary>
        private static void AddSegment(
            List<MotionSegment> segments, string name,
            ref double currentTime, double duration,
            double startAcc, double endAcc, double jerkValue)
        {
            // 跳过时长为0的段（简化输出）
            if (duration < 1e-9) return;
            segments.Add(new MotionSegment
            {
                Name = name,
                StartTime = currentTime,
                EndTime = currentTime + duration,
                StartVelocity = 0,   // 由外部逻辑计算，此处简化
                EndVelocity = 0,
                Acceleration = (startAcc + endAcc) / 2.0,  // 平均加速度
                Distance = 0   // 由采样点积分得到
            });
            currentTime += duration;
        }

        /// <summary>
        /// 数值积分求S曲线在时刻 t 的位置和速度（分7段积分）
        /// </summary>
        private static (double pos, double vel, double acc) IntegrateState(
            double t, double totalTime, double distance,
            double peakV, double ta, double tc, double td,
            double tj, double t2, double amax, double jerk)
        {
            // 边界处理
            if (t <= 0) return (0, 0, 0);
            if (t >= totalTime) return (distance, 0, 0);

            // 各段起始时刻（从运动开始计算）
            double t_s1 = 0;            // 段1开始
            double t_s2 = tj;           // 段2开始
            double t_s3 = tj + t2;      // 段3开始
            double t_s4 = ta;           // 段4开始（匀速段）
            double t_s5 = ta + tc;      // 段5开始
            double t_s6 = ta + tc + tj; // 段6开始
            double t_s7 = ta + tc + tj + t2; // 段7开始

            double pos = 0, vel = 0, acc = 0;

            // 段1：加加速（0 ~ tj）
            void IntegSeg1(double dt)
            {
                // acc(t) = jerk*dt, vel(t) = 0.5*jerk*dt², pos = jerk*dt³/6
                acc = jerk * dt;
                vel = 0.5 * jerk * dt * dt;
                pos = jerk * dt * dt * dt / 6.0;
            }

            // 段2：匀加速（tj ~ tj+t2）
            void IntegSeg2(double dt, double v0_seg2)
            {
                acc = amax;
                vel = v0_seg2 + amax * dt;
                double s0 = jerk * tj * tj * tj / 6.0;
                double v_seg2_start = 0.5 * jerk * tj * tj;
                pos = s0 + v_seg2_start * dt + 0.5 * amax * dt * dt;
            }

            // 段3：减加速（tj+t2 ~ ta = 2tj+t2）
            void IntegSeg3(double dt, double v0_seg3, double s0_seg3)
            {
                // acc(t) = amax - jerk*dt
                acc = amax - jerk * dt;
                vel = v0_seg3 + amax * dt - 0.5 * jerk * dt * dt;
                pos = s0_seg3 + v0_seg3 * dt + 0.5 * amax * dt * dt - jerk * dt * dt * dt / 6.0;
            }

            // 各段参数预算
            double v_after_s1 = 0.5 * jerk * tj * tj;                       // 段1末速度
            double v_after_s2 = v_after_s1 + amax * t2;                     // 段2末速度（=peakV）
            double s_after_s1 = jerk * tj * tj * tj / 6.0;                  // 段1末位置
            double s_after_s2 = s_after_s1 + v_after_s1 * t2 + 0.5 * amax * t2 * t2; // 段2末位置
            double s_after_s3 = s_after_s2 + v_after_s2 * tj + 0.5 * amax * tj * tj - jerk * tj * tj * tj / 6.0; // 段3末位置

            double s_const_end = s_after_s3 + peakV * tc;  // 匀速段末位置

            if (t <= t_s2)
            {
                IntegSeg1(t - t_s1);
            }
            else if (t <= t_s3)
            {
                IntegSeg2(t - t_s2, v_after_s1);
                // 段2不单独算，直接用公式
                acc = amax;
                vel = v_after_s1 + amax * (t - t_s2);
                double dt2 = t - t_s2;
                pos = s_after_s1 + v_after_s1 * dt2 + 0.5 * amax * dt2 * dt2;
            }
            else if (t <= t_s4)
            {
                IntegSeg3(t - t_s3, v_after_s2, s_after_s2);
            }
            else if (t <= t_s5)
            {
                // 匀速段
                acc = 0;
                vel = peakV;
                pos = s_after_s3 + peakV * (t - t_s4);
            }
            else if (t <= t_s6)
            {
                // 段5：加减速（Jerk = -J）
                double dt5 = t - t_s5;
                acc = -jerk * dt5;
                vel = peakV - 0.5 * jerk * dt5 * dt5;
                pos = s_const_end + peakV * dt5 - jerk * dt5 * dt5 * dt5 / 6.0;
            }
            else if (t <= t_s7)
            {
                // 段6：匀减速
                double v_after_s5 = peakV - 0.5 * jerk * tj * tj;
                double s_after_s5 = s_const_end + peakV * tj - jerk * tj * tj * tj / 6.0;
                double dt6 = t - t_s6;
                acc = -amax;
                vel = v_after_s5 - amax * dt6;
                pos = s_after_s5 + v_after_s5 * dt6 - 0.5 * amax * dt6 * dt6;
            }
            else
            {
                // 段7：减减速（Jerk = +J）
                double v_after_s5 = peakV - 0.5 * jerk * tj * tj;
                double s_after_s5 = s_const_end + peakV * tj - jerk * tj * tj * tj / 6.0;
                double v_after_s6 = v_after_s5 - amax * t2;
                double s_after_s6 = s_after_s5 + v_after_s5 * t2 - 0.5 * amax * t2 * t2;
                double dt7 = t - t_s7;
                acc = -amax + jerk * dt7;
                vel = v_after_s6 - amax * dt7 + 0.5 * jerk * dt7 * dt7;
                pos = s_after_s6 + v_after_s6 * dt7 - 0.5 * amax * dt7 * dt7 + jerk * dt7 * dt7 * dt7 / 6.0;
            }

            // 限制到合法范围（数值精度保护）
            pos = Math.Clamp(pos, 0, distance);
            vel = Math.Max(0, vel);
            return (pos, vel, acc);
        }

        /// <summary>
        /// 二分法求解距离不足时的峰值速度
        /// 目标：找到 v_peak 使得 2 * sa(v_peak) = distance
        /// </summary>
        private static double SolvePeakVelocity(double distance, double maxAcceleration, double jerk)
        {
            double lo = 0.0, hi = Math.Sqrt(distance * jerk); // 上界（粗估）
            // 迭代最多50次
            for (int i = 0; i < 50; i++)
            {
                double mid = (lo + hi) / 2.0;
                double tj = Math.Min(maxAcceleration / jerk, Math.Sqrt(mid / jerk));
                double amax = jerk * tj;
                double t2 = amax > 0 ? Math.Max(0, (mid - 2 * amax * tj) / amax) : 0;
                double ta = 2 * tj + t2;
                double sa = mid * ta / 2.0; // 加速阶段位移近似（精确到小数点5位以上）
                double totalSa = 2 * sa;   // 加+减速

                if (Math.Abs(totalSa - distance) < 1e-6) break;
                if (totalSa < distance) lo = mid;
                else hi = mid;
            }
            return (lo + hi) / 2.0;
        }
    }
}
