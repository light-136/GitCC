// ============================================================
// 文件：TrapezoidalProfile.cs
// 用途：梯形速度曲线运动规划 — 实现 IMotionProfile 接口
// 设计思路：
//   梯形速度曲线是工业运动控制中最基础的速度规划方式。
//   运动分为三段：加速段→匀速段→减速段，速度曲线呈梯形。
//   当运动距离较短、无法达到最大速度时，自动退化为三角形
//   曲线（去掉匀速段），确保运动平滑且不超限。
//
// 运动学公式：
//   加速段：v = a*t,  s = 0.5*a*t²
//   匀速段：v = Vmax, s = Vmax*t
//   减速段：v = Vmax - a*t, s = Vmax*t - 0.5*a*t²
//
// 三角形退化条件：
//   当 totalDistance < Vmax²/a 时，无法达到最大速度，
//   此时峰值速度 Vpeak = sqrt(a * totalDistance)
// ============================================================

using SmartMES.Core.Interfaces;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 梯形速度曲线运动规划器。
    /// <para>
    /// 将一段点到点运动分解为"加速→匀速→减速"三个阶段，
    /// 在满足最大速度和最大加速度约束的前提下，以最短时间完成运动。
    /// 当距离不足以达到最大速度时，自动退化为三角形曲线。
    /// </para>
    /// </summary>
    public class TrapezoidalProfile : IMotionProfile
    {
        /// <summary>运动规划类型标识：梯形曲线。</summary>
        public MotionProfileType ProfileType => MotionProfileType.Trapezoidal;

        /// <summary>
        /// 计算指定时刻的运动状态。
        /// </summary>
        /// <param name="elapsedSeconds">从运动开始经过的时间（秒）。</param>
        /// <param name="totalDistance">总运动距离（mm），始终为正值。</param>
        /// <param name="maxVelocity">最大允许速度（mm/s）。</param>
        /// <param name="maxAcceleration">最大允许加速度（mm/s²）。</param>
        /// <returns>该时刻的运动状态快照。</returns>
        public MotionState Calculate(double elapsedSeconds, double totalDistance,
                                     double maxVelocity, double maxAcceleration)
        {
            // 参数校验：距离为零或负值时直接返回完成状态
            if (totalDistance <= 0 || maxVelocity <= 0 || maxAcceleration <= 0)
            {
                return new MotionState
                {
                    Position = 0,
                    Velocity = 0,
                    Acceleration = 0,
                    Jerk = 0,
                    IsComplete = true
                };
            }

            // 计算各段时间参数
            var (tAccel, tCruise, tDecel, peakVelocity) = ComputePhases(totalDistance, maxVelocity, maxAcceleration);
            double totalTime = tAccel + tCruise + tDecel;

            // 时间超出总运动时间，返回终点状态
            if (elapsedSeconds >= totalTime)
            {
                return new MotionState
                {
                    Position = totalDistance,
                    Velocity = 0,
                    Acceleration = 0,
                    Jerk = 0,
                    IsComplete = true
                };
            }

            // 时间为负值，返回起点状态
            if (elapsedSeconds <= 0)
            {
                return new MotionState
                {
                    Position = 0,
                    Velocity = 0,
                    Acceleration = maxAcceleration, // 即将开始加速
                    Jerk = 0,
                    IsComplete = false
                };
            }

            double position, velocity, acceleration;

            if (elapsedSeconds <= tAccel)
            {
                // ========== 第一段：加速段 ==========
                // 运动学公式：v = a*t, s = 0.5*a*t²
                double t = elapsedSeconds;
                acceleration = maxAcceleration;
                velocity = maxAcceleration * t;
                position = 0.5 * maxAcceleration * t * t;
            }
            else if (elapsedSeconds <= tAccel + tCruise)
            {
                // ========== 第二段：匀速段 ==========
                // 运动学公式：v = Vpeak（常数），s = s_accel + Vpeak * t
                double t = elapsedSeconds - tAccel;
                // 加速段结束时的位移
                double sAccel = 0.5 * maxAcceleration * tAccel * tAccel;
                acceleration = 0;
                velocity = peakVelocity;
                position = sAccel + peakVelocity * t;
            }
            else
            {
                // ========== 第三段：减速段 ==========
                // 运动学公式：v = Vpeak - a*t, s = s_prev + Vpeak*t - 0.5*a*t²
                double t = elapsedSeconds - tAccel - tCruise;
                // 加速段 + 匀速段结束时的位移
                double sAccel = 0.5 * maxAcceleration * tAccel * tAccel;
                double sCruise = peakVelocity * tCruise;
                acceleration = -maxAcceleration;
                velocity = peakVelocity - maxAcceleration * t;
                position = sAccel + sCruise + peakVelocity * t - 0.5 * maxAcceleration * t * t;

                // 防止数值误差导致速度为负
                if (velocity < 0) velocity = 0;
            }

            // 防止数值误差导致位置超出范围
            if (position > totalDistance) position = totalDistance;
            if (position < 0) position = 0;

            return new MotionState
            {
                Position = position,
                Velocity = velocity,
                Acceleration = acceleration,
                Jerk = 0, // 梯形曲线无加加速度（jerk 为无穷大的脉冲，此处简化为0）
                IsComplete = false
            };
        }

        /// <summary>
        /// 计算完成整段运动所需的总时间（秒）。
        /// </summary>
        /// <param name="totalDistance">总运动距离（mm）。</param>
        /// <param name="maxVelocity">最大允许速度（mm/s）。</param>
        /// <param name="maxAcceleration">最大允许加速度（mm/s²）。</param>
        /// <returns>总运动时间（秒）。</returns>
        public double GetTotalTime(double totalDistance, double maxVelocity, double maxAcceleration)
        {
            if (totalDistance <= 0 || maxVelocity <= 0 || maxAcceleration <= 0)
                return 0;

            var (tAccel, tCruise, tDecel, _) = ComputePhases(totalDistance, maxVelocity, maxAcceleration);
            return tAccel + tCruise + tDecel;
        }

        /// <summary>
        /// 计算梯形曲线的三段时间参数和峰值速度。
        /// <para>
        /// 算法逻辑：
        /// 1. 先假设能达到最大速度，计算加速距离 = Vmax²/(2a)
        /// 2. 如果 2 × 加速距离 > 总距离，说明无法达到最大速度，退化为三角形
        /// 3. 三角形情况下，峰值速度 = sqrt(a × totalDistance)
        /// </para>
        /// </summary>
        /// <param name="totalDistance">总运动距离。</param>
        /// <param name="maxVelocity">最大允许速度。</param>
        /// <param name="maxAcceleration">最大允许加速度。</param>
        /// <returns>
        /// tAccel: 加速段时间，tCruise: 匀速段时间，tDecel: 减速段时间，peakVelocity: 实际峰值速度。
        /// </returns>
        private static (double tAccel, double tCruise, double tDecel, double peakVelocity)
            ComputePhases(double totalDistance, double maxVelocity, double maxAcceleration)
        {
            // 加速到最大速度所需的距离：s_accel = Vmax² / (2a)
            double accelDistance = maxVelocity * maxVelocity / (2.0 * maxAcceleration);

            // 加速 + 减速所需的总距离（对称梯形，加速距离 = 减速距离）
            double accelPlusDecelDistance = 2.0 * accelDistance;

            double tAccel, tCruise, tDecel, peakVelocity;

            if (accelPlusDecelDistance >= totalDistance)
            {
                // ===== 三角形退化 =====
                // 无法达到最大速度，峰值速度由距离决定
                // 推导：totalDistance = 2 × (Vpeak² / 2a) → Vpeak = sqrt(a × totalDistance)
                peakVelocity = Math.Sqrt(maxAcceleration * totalDistance);
                tAccel = peakVelocity / maxAcceleration;  // 加速时间 = Vpeak / a
                tDecel = tAccel;                           // 对称减速
                tCruise = 0;                               // 无匀速段
            }
            else
            {
                // ===== 标准梯形 =====
                peakVelocity = maxVelocity;
                tAccel = maxVelocity / maxAcceleration;                          // 加速时间
                tDecel = tAccel;                                                  // 减速时间（对称）
                double cruiseDistance = totalDistance - accelPlusDecelDistance;     // 匀速段距离
                tCruise = cruiseDistance / maxVelocity;                            // 匀速段时间
            }

            return (tAccel, tCruise, tDecel, peakVelocity);
        }
    }
}
