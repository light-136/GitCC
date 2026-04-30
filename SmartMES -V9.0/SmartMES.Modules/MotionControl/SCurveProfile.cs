// ============================================================
// 文件：SCurveProfile.cs
// 用途：S曲线（加加速度限制）运动规划 — 实现 IMotionProfile 接口
// 设计思路：
//   S曲线分7段：加加速→匀加速→减加速→匀速→加减速→匀减速→减减速
//   短距离退化：先去匀速段，再去匀加速段
//   每段公式：a(t)=a0+J*t, v(t)=v0+a0*t+0.5*J*t², s(t)=s0+v0*t+0.5*a0*t²+(1/6)*J*t³
// ============================================================

using SmartMES.Core.Interfaces;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// S曲线运动规划器 — 7段式平滑运动曲线，通过限制Jerk减少振动。
    /// </summary>
    public class SCurveProfile : IMotionProfile
    {
        /// <summary>运动规划类型标识：S曲线。</summary>
        public MotionProfileType ProfileType => MotionProfileType.SCurve;

        /// <summary>最大加加速度（Jerk），单位 mm/s³。</summary>
        public double MaxJerk { get; set; } = 50000.0;

        /// <summary>计算指定时刻的运动状态（含 Jerk 值）。</summary>
        public MotionState Calculate(double elapsedSeconds, double totalDistance,
                                     double maxVelocity, double maxAcceleration)
        {
            if (totalDistance <= 0 || maxVelocity <= 0 || maxAcceleration <= 0 || MaxJerk <= 0)
                return new MotionState { IsComplete = true };

            var (dur, jv) = ComputePhases(totalDistance, maxVelocity, maxAcceleration, MaxJerk);
            double totalTime = 0;
            for (int i = 0; i < 7; i++) totalTime += dur[i];

            if (elapsedSeconds >= totalTime)
                return new MotionState { Position = totalDistance, IsComplete = true };
            if (elapsedSeconds <= 0)
                return new MotionState { Jerk = jv[0], IsComplete = false };

            // 逐段累积运动学状态，找到当前时刻所在段
            double pos = 0, vel = 0, acc = 0, rem = elapsedSeconds;
            for (int i = 0; i < 7; i++)
            {
                double dt = dur[i];
                if (dt <= 1e-12) continue;
                double j = jv[i];
                if (rem <= dt + 1e-12)
                {
                    double t = Math.Min(rem, dt);
                    // 段内运动学公式
                    double cA = acc + j * t;
                    double cV = vel + acc * t + 0.5 * j * t * t;
                    double cP = pos + vel * t + 0.5 * acc * t * t + (1.0 / 6.0) * j * t * t * t;
                    if (cV < 0) cV = 0;
                    if (cP > totalDistance) cP = totalDistance;
                    return new MotionState
                    {
                        Position = cP, Velocity = cV, Acceleration = cA, Jerk = j, IsComplete = false
                    };
                }
                // 累积整段末状态
                pos += vel * dt + 0.5 * acc * dt * dt + (1.0 / 6.0) * j * dt * dt * dt;
                vel += acc * dt + 0.5 * j * dt * dt;
                acc += j * dt;
                rem -= dt;
            }
            return new MotionState { Position = totalDistance, IsComplete = true };
        }

        /// <summary>计算完成整段运动所需的总时间（秒）。</summary>
        public double GetTotalTime(double totalDistance, double maxVelocity, double maxAcceleration)
        {
            if (totalDistance <= 0 || maxVelocity <= 0 || maxAcceleration <= 0 || MaxJerk <= 0)
                return 0;
            var (dur, _) = ComputePhases(totalDistance, maxVelocity, maxAcceleration, MaxJerk);
            double total = 0;
            for (int i = 0; i < 7; i++) total += dur[i];
            return total;
        }

        /// <summary>计算S曲线7段的时间和Jerk值。</summary>
        private static (double[] dur, double[] jv) ComputePhases(
            double totalDist, double vMax, double aMax, double jMax)
        {
            // Tj = 加速度从0到aMax所需时间
            double tj = aMax / jMax;
            if (jMax * tj * tj > vMax) tj = Math.Sqrt(vMax / jMax);
            double aR = jMax * tj; // 实际峰值加速度
            // 匀加速段时间
            double t2 = (aR > 1e-12 && aR * tj < vMax) ? (vMax / aR) - tj : 0;
            if (t2 < 0) t2 = 0;
            double vPeak = aR * (tj + t2);
            double sAccel = CalcAccelDist(tj, t2, aR, jMax);
            double t4;
            if (2.0 * sAccel >= totalDist)
            {
                // 二分搜索合适的峰值速度
                t4 = 0;
                double vLo = 0, vHi = vPeak;
                for (int it = 0; it < 100; it++)
                {
                    double vT = (vLo + vHi) * 0.5;
                    Recalc(vT, aMax, jMax, out double tjT, out double t2T, out double aT);
                    if (2.0 * CalcAccelDist(tjT, t2T, aT, jMax) > totalDist) vHi = vT; else vLo = vT;
                    if (vHi - vLo < 1e-9) break;
                }
                vPeak = (vLo + vHi) * 0.5;
                Recalc(vPeak, aMax, jMax, out tj, out t2, out aR);
            }
            else
            {
                t4 = (vPeak > 1e-12) ? (totalDist - 2.0 * sAccel) / vPeak : 0;
            }
            return (new[] { tj, t2, tj, t4, tj, t2, tj },
                    new[] { jMax, 0.0, -jMax, 0.0, -jMax, 0.0, jMax });
        }

        /// <summary>计算加速阶段（段1+2+3）总距离。</summary>
        private static double CalcAccelDist(double tj, double t2, double aR, double jMax)
        {
            double s1 = (1.0 / 6.0) * jMax * tj * tj * tj;
            double v1 = 0.5 * jMax * tj * tj;
            double s2 = v1 * t2 + 0.5 * aR * t2 * t2;
            double v2 = v1 + aR * t2;
            double s3 = v2 * tj + 0.5 * aR * tj * tj - (1.0 / 6.0) * jMax * tj * tj * tj;
            return s1 + s2 + s3;
        }

        /// <summary>根据目标峰值速度重新计算Tj和T2。</summary>
        private static void Recalc(double vP, double aMax, double jMax,
            out double tj, out double t2, out double aR)
        {
            tj = aMax / jMax;
            if (jMax * tj * tj > vP)
            { tj = Math.Sqrt(Math.Max(0, vP / jMax)); t2 = 0; aR = jMax * tj; }
            else
            { aR = jMax * tj; t2 = (aR > 1e-12) ? (vP / aR) - tj : 0; if (t2 < 0) t2 = 0; }
        }
    }
}
