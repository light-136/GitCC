// ============================================================
// 文件：IMotionProfile.cs
// 用途：运动规划接口定义 — 统一梯形与S曲线运动规划
// 设计思路：
//   将运动规划算法抽象为接口，使 AxisController 可以
//   在运行时切换不同的运动曲线（梯形/S曲线），而无需
//   修改控制器本身的代码。
// ============================================================

namespace SmartMES.Core.Interfaces
{
    /// <summary>
    /// 运动规划类型枚举。
    /// </summary>
    public enum MotionProfileType
    {
        /// <summary>梯形速度曲线 — 加速→匀速→减速，速度有突变。</summary>
        Trapezoidal,

        /// <summary>S曲线（加加速度限制）— 7段式平滑运动，无速度突变。</summary>
        SCurve
    }

    /// <summary>
    /// 运动状态快照 — 描述某一时刻轴的运动学参数。
    /// </summary>
    public class MotionState
    {
        /// <summary>当前位置（相对于起点的位移），单位 mm。</summary>
        public double Position { get; set; }

        /// <summary>当前速度，单位 mm/s。</summary>
        public double Velocity { get; set; }

        /// <summary>当前加速度，单位 mm/s²。</summary>
        public double Acceleration { get; set; }

        /// <summary>当前加加速度（jerk），单位 mm/s³。仅S曲线有意义。</summary>
        public double Jerk { get; set; }

        /// <summary>运动是否已完成（到达目标位置）。</summary>
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// 运动规划接口 — 根据运动学参数计算任意时刻的运动状态。
    /// 实现类：TrapezoidalProfile（梯形）、SCurveProfile（S曲线）。
    /// </summary>
    public interface IMotionProfile
    {
        /// <summary>运动规划类型标识。</summary>
        MotionProfileType ProfileType { get; }

        /// <summary>
        /// 计算指定时刻的运动状态。
        /// </summary>
        /// <param name="elapsedSeconds">从运动开始经过的时间（秒）。</param>
        /// <param name="totalDistance">总运动距离（mm），始终为正值。</param>
        /// <param name="maxVelocity">最大允许速度（mm/s）。</param>
        /// <param name="maxAcceleration">最大允许加速度（mm/s²）。</param>
        /// <returns>该时刻的运动状态快照。</returns>
        MotionState Calculate(double elapsedSeconds, double totalDistance,
                              double maxVelocity, double maxAcceleration);

        /// <summary>
        /// 计算完成整段运动所需的总时间（秒）。
        /// </summary>
        double GetTotalTime(double totalDistance, double maxVelocity, double maxAcceleration);
    }

    /// <summary>
    /// 坐标系管理接口 — 负责机械坐标与工件坐标之间的变换。
    /// 支持 G54~G59 工件坐标系和工具坐标偏移。
    /// </summary>
    public interface ICoordinateManager
    {
        /// <summary>
        /// 设置指定坐标系的偏移参数。
        /// </summary>
        /// <param name="cs">目标坐标系（G54~G59 或 Tool）。</param>
        /// <param name="transform">坐标变换参数（偏移+旋转）。</param>
        void SetWorkOffset(Models.CoordinateSystem cs, Models.CoordinateTransform transform);

        /// <summary>
        /// 获取指定坐标系的偏移参数。
        /// </summary>
        Models.CoordinateTransform GetWorkOffset(Models.CoordinateSystem cs);

        /// <summary>
        /// 将机械坐标转换为工件坐标。
        /// </summary>
        (double X, double Y, double Z) MachineToWork(
            double mx, double my, double mz, Models.CoordinateSystem cs);

        /// <summary>
        /// 将工件坐标转换为机械坐标。
        /// </summary>
        (double X, double Y, double Z) WorkToMachine(
            double wx, double wy, double wz, Models.CoordinateSystem cs);
    }
}
