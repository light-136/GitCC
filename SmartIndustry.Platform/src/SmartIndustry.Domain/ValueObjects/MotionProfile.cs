// ============================================================
// 文件：MotionProfile.cs
// 层次：领域层 (Domain Layer) — 值对象
// 职责：封装单次运动指令的速度/加速度/减速度/急停减速度参数
// 设计思路：
//   运动参数是描述"如何运动"而非"运动到哪里"的参数集合，
//   天然是值对象（无身份，按内容比较相等）。
//   使用 readonly record struct 保证：不可变性、栈分配、自动相等比较。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Domain.ValueObjects
{
    /// <summary>
    /// 运动参数值对象，封装轴运动的速度规划参数。
    /// 单位说明：速度（mm/s 或 pulse/s）、加速度（mm/s² 或 pulse/s²），
    /// 具体单位由运动控制层配置决定，此类本身不耦合单位。
    /// </summary>
    public readonly record struct MotionProfile
    {
        /// <summary>最大运动速度（量值，需 > 0）</summary>
        public double MaxVelocity { get; init; }

        /// <summary>加速阶段加速度（量值，需 > 0）</summary>
        public double Acceleration { get; init; }

        /// <summary>减速阶段减速度（量值，需 > 0）</summary>
        public double Deceleration { get; init; }

        /// <summary>急停减速度（量值，通常远大于 Deceleration）</summary>
        public double EmergencyDeceleration { get; init; }

        /// <summary>
        /// 创建运动参数值对象。
        /// </summary>
        /// <param name="maxVelocity">最大速度（> 0）</param>
        /// <param name="acceleration">加速度（> 0）</param>
        /// <param name="deceleration">减速度（> 0）</param>
        /// <param name="emergencyDeceleration">急停减速度（> 0，默认为减速度的5倍）</param>
        public MotionProfile(double maxVelocity, double acceleration, double deceleration, double emergencyDeceleration = 0)
        {
            if (maxVelocity <= 0) throw new ArgumentOutOfRangeException(nameof(maxVelocity), "最大速度必须大于零");
            if (acceleration <= 0) throw new ArgumentOutOfRangeException(nameof(acceleration), "加速度必须大于零");
            if (deceleration <= 0) throw new ArgumentOutOfRangeException(nameof(deceleration), "减速度必须大于零");

            MaxVelocity = maxVelocity;
            Acceleration = acceleration;
            Deceleration = deceleration;
            // 急停减速度默认为减速度的5倍，若未指定则自动计算
            EmergencyDeceleration = emergencyDeceleration > 0 ? emergencyDeceleration : deceleration * 5.0;
        }

        /// <summary>默认运动参数：速度100、加减速1000、急停5000（单位视硬件配置而定）</summary>
        public static readonly MotionProfile Default = new(100.0, 1000.0, 1000.0, 5000.0);

        /// <summary>慢速点动参数：速度10，加减速500</summary>
        public static readonly MotionProfile Jog = new(10.0, 500.0, 500.0, 2500.0);

        public override string ToString()
            => $"[速度={MaxVelocity}, 加速={Acceleration}, 减速={Deceleration}, 急停={EmergencyDeceleration}]";
    }
}
