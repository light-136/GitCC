// ============================================================
// 文件：MotionModels.cs
// 用途：运动控制领域模型 — 圆弧参数、坐标系、回零策略、
//       电子齿轮/凸轮等数据结构定义
// 设计思路：
//   将运动控制中常用的数据结构集中定义在 Core 层，
//   使 Modules 和 Services 都能引用，避免循环依赖。
// ============================================================

using SmartMES.Core.Interfaces;

namespace SmartMES.Core.Models
{
    // ======================== 插补类型 ========================

    /// <summary>
    /// 插补运动类型。
    /// </summary>
    public enum InterpolationType
    {
        /// <summary>直线插补（G0/G1）。</summary>
        Linear,

        /// <summary>顺时针圆弧插补（G2）。</summary>
        CircularCW,

        /// <summary>逆时针圆弧插补（G3）。</summary>
        CircularCCW
    }

    /// <summary>
    /// 圆弧插补参数 — 描述一段圆弧的几何信息。
    /// 支持两种定义方式：I/J 圆心偏移法 和 R 半径法。
    /// </summary>
    public class ArcParameters
    {
        /// <summary>圆心相对于起点的 X 偏移（I 参数）。</summary>
        public double CenterI { get; set; }

        /// <summary>圆心相对于起点的 Y 偏移（J 参数）。</summary>
        public double CenterJ { get; set; }

        /// <summary>圆弧半径（R 参数），正值=劣弧，负值=优弧。</summary>
        public double Radius { get; set; }

        /// <summary>是否使用 R 参数模式（否则使用 I/J 模式）。</summary>
        public bool UseRadiusMode { get; set; }

        /// <summary>起始角度（弧度）。</summary>
        public double StartAngle { get; set; }

        /// <summary>终止角度（弧度）。</summary>
        public double EndAngle { get; set; }

        /// <summary>是否为顺时针方向。</summary>
        public bool IsClockwise { get; set; }
    }

    // ======================== 坐标系 ========================

    /// <summary>
    /// 坐标系类型 — 对应 G 代码中的坐标系选择指令。
    /// </summary>
    public enum CoordinateSystem
    {
        /// <summary>机械坐标系（绝对参考）。</summary>
        Machine = 0,

        /// <summary>工件坐标系 1（G54）。</summary>
        Work1 = 54,

        /// <summary>工件坐标系 2（G55）。</summary>
        Work2 = 55,

        /// <summary>工件坐标系 3（G56）。</summary>
        Work3 = 56,

        /// <summary>工件坐标系 4（G57）。</summary>
        Work4 = 57,

        /// <summary>工件坐标系 5（G58）。</summary>
        Work5 = 58,

        /// <summary>工件坐标系 6（G59）。</summary>
        Work6 = 59,

        /// <summary>工具坐标系。</summary>
        Tool = 100
    }

    /// <summary>
    /// 坐标变换参数 — 描述一个坐标系相对于机械坐标系的偏移和旋转。
    /// </summary>
    public class CoordinateTransform
    {
        /// <summary>X 方向偏移（mm）。</summary>
        public double OffsetX { get; set; }

        /// <summary>Y 方向偏移（mm）。</summary>
        public double OffsetY { get; set; }

        /// <summary>Z 方向偏移（mm）。</summary>
        public double OffsetZ { get; set; }

        /// <summary>绕 Z 轴旋转角度（度）。</summary>
        public double RotationDeg { get; set; }
    }

    // ======================== 回零策略 ========================

    /// <summary>
    /// 回零搜索策略 — 不同的回零方式适用于不同的机械结构。
    /// </summary>
    public enum HomingStrategy
    {
        /// <summary>传感器回零：快速搜索原点传感器，慢速精确定位。</summary>
        SensorBased,

        /// <summary>编码器脉冲回零：在传感器基础上继续搜索编码器 Z 脉冲。</summary>
        IndexPulse,

        /// <summary>硬限位回零：慢速运动直到检测到堵转。</summary>
        HardStop,

        /// <summary>当前位置回零：直接将当前位置设为零点。</summary>
        CurrentPosition
    }

    /// <summary>
    /// 回零配置参数。
    /// </summary>
    public class HomingConfig
    {
        /// <summary>回零策略。</summary>
        public HomingStrategy Strategy { get; set; } = HomingStrategy.SensorBased;

        /// <summary>快速搜索速度（mm/s）。</summary>
        public double SearchVelocity { get; set; } = 50.0;

        /// <summary>慢速精确定位速度（mm/s）。</summary>
        public double CreepVelocity { get; set; } = 5.0;

        /// <summary>回零后的偏移量（mm）。</summary>
        public double Offset { get; set; } = 0.0;

        /// <summary>回零超时时间（毫秒）。</summary>
        public int TimeoutMs { get; set; } = 30000;
    }

    // ======================== 电子齿轮与凸轮 ========================

    /// <summary>
    /// 电子齿轮配置 — 从轴跟随主轴运动。
    /// </summary>
    public class GearingConfig
    {
        /// <summary>主轴名称。</summary>
        public string MasterAxisName { get; set; } = string.Empty;

        /// <summary>齿轮比（从轴位移 = 主轴位移 × Ratio）。</summary>
        public double Ratio { get; set; } = 1.0;

        /// <summary>相位偏移（mm）。</summary>
        public double PhaseOffset { get; set; } = 0.0;
    }

    /// <summary>
    /// 凸轮表中的一个点 — 定义主轴位置与从轴位置的对应关系。
    /// </summary>
    public class CamPoint
    {
        /// <summary>主轴位置（mm 或 度）。</summary>
        public double MasterPosition { get; set; }

        /// <summary>从轴位置（mm 或 度）。</summary>
        public double SlavePosition { get; set; }
    }

    /// <summary>
    /// 凸轮曲线 — 由一系列凸轮点定义的主从轴运动关系。
    /// </summary>
    public class CamProfile
    {
        /// <summary>凸轮点列表（按主轴位置升序排列）。</summary>
        public List<CamPoint> Points { get; set; } = new();

        /// <summary>是否为周期性凸轮（到达末尾后循环）。</summary>
        public bool IsCyclic { get; set; }

        /// <summary>一个周期的主轴行程长度。</summary>
        public double CycleLength { get; set; } = 360.0;
    }

    // ======================== 30轴系统扩展模型 ========================

    /// <summary>
    /// 轴类型 — 区分直线轴与旋转轴，影响单位和运动学计算。
    /// </summary>
    public enum AxisType
    {
        /// <summary>直线轴（单位：mm）。</summary>
        Linear,

        /// <summary>旋转轴（单位：度）。</summary>
        Rotary,

        /// <summary>主轴（单位：rpm）。</summary>
        Spindle
    }

    /// <summary>
    /// 轴配置 — 描述单个运动轴的完整参数集。
    /// 用于 30 轴系统中替代原来的内联元组配置。
    /// </summary>
    public class AxisConfig
    {
        /// <summary>轴名称（唯一标识符，如"X1","Y2","A1"）。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>轴类型。</summary>
        public AxisType AxisType { get; set; } = AxisType.Linear;

        /// <summary>最大速度（mm/s 或 °/s）。</summary>
        public double MaxVelocity { get; set; } = 500.0;

        /// <summary>最大加速度（mm/s² 或 °/s²）。</summary>
        public double MaxAcceleration { get; set; } = 2000.0;

        /// <summary>最大加加速度（mm/s³），仅 S 曲线使用。</summary>
        public double MaxJerk { get; set; } = 50000.0;

        /// <summary>软正限位（mm 或 °）。</summary>
        public double SoftLimitPositive { get; set; } = double.MaxValue;

        /// <summary>软负限位（mm 或 °）。</summary>
        public double SoftLimitNegative { get; set; } = double.MinValue;

        /// <summary>回零配置。</summary>
        public HomingConfig HomingConfig { get; set; } = new();

        /// <summary>所属轴组名称（空字符串=未分组）。</summary>
        public string GroupName { get; set; } = string.Empty;

        /// <summary>所属通道编号（0=默认通道）。</summary>
        public int ChannelId { get; set; } = 0;

        /// <summary>运动规划类型。</summary>
        public MotionProfileType ProfileType { get; set; } = MotionProfileType.SCurve;
    }

    /// <summary>
    /// 轴组类型 — 定义轴组的运动学关系。
    /// </summary>
    public enum AxisGroupType
    {
        /// <summary>XYZ 笛卡尔坐标组。</summary>
        Cartesian,

        /// <summary>龙门双驱同步组（两个轴同步运动）。</summary>
        Gantry,

        /// <summary>旋转工作台组。</summary>
        Rotary,

        /// <summary>自定义轴组。</summary>
        Custom
    }

    /// <summary>
    /// 轴组定义 — 将多个轴组织为协调运动的组。
    /// 同一组内的轴共享插补空间，可以进行多轴联动。
    /// </summary>
    public class AxisGroupDefinition
    {
        /// <summary>轴组名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>轴组类型。</summary>
        public AxisGroupType GroupType { get; set; } = AxisGroupType.Cartesian;

        /// <summary>组内轴名称列表。</summary>
        public List<string> AxisNames { get; set; } = new();

        /// <summary>该组使用的坐标系。</summary>
        public CoordinateSystem CoordinateSystem { get; set; } = CoordinateSystem.Machine;

        /// <summary>最大插补速度（mm/s）。</summary>
        public double MaxInterpolationVelocity { get; set; } = 500.0;
    }

    /// <summary>
    /// 通道状态 — 描述单个 G 代码通道的运行状态。
    /// </summary>
    public enum ChannelState
    {
        /// <summary>空闲 — 无程序在执行。</summary>
        Idle,

        /// <summary>运行中 — 正在执行 G 代码程序。</summary>
        Running,

        /// <summary>暂停 — 程序被暂停。</summary>
        Paused,

        /// <summary>错误 — 发生错误，需要复位。</summary>
        Error
    }

    /// <summary>
    /// 通道配置 — 定义一个独立的 G 代码执行通道。
    /// 多通道系统中，每个通道独立解析和执行 G 代码程序，
    /// 但通道之间可以通过同步指令协调。
    /// </summary>
    public class ChannelConfig
    {
        /// <summary>通道编号（从0开始）。</summary>
        public int Id { get; set; }

        /// <summary>通道名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>该通道管理的轴名称列表。</summary>
        public List<string> AxisNames { get; set; } = new();

        /// <summary>默认进给速度（mm/min）。</summary>
        public double DefaultFeedRate { get; set; } = 1000.0;
    }

    /// <summary>
    /// 碰撞区域定义 — 描述两个轴组之间可能发生碰撞的空间区域。
    /// 当两组轴同时进入该区域时，系统会触发安全互锁。
    /// </summary>
    public class CollisionZone
    {
        /// <summary>碰撞区域名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>第一个轴名。</summary>
        public string Axis1Name { get; set; } = string.Empty;

        /// <summary>第二个轴名。</summary>
        public string Axis2Name { get; set; } = string.Empty;

        /// <summary>轴1的安全范围下限。</summary>
        public double Axis1Min { get; set; }

        /// <summary>轴1的安全范围上限。</summary>
        public double Axis1Max { get; set; }

        /// <summary>轴2的安全范围下限。</summary>
        public double Axis2Min { get; set; }

        /// <summary>轴2的安全范围上限。</summary>
        public double Axis2Max { get; set; }

        /// <summary>最小安全距离（mm）。</summary>
        public double MinSafeDistance { get; set; } = 5.0;

        /// <summary>是否启用该碰撞区域。</summary>
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// 轴性能快照 — 记录单个轴在特定时间窗口内的性能指标。
    /// </summary>
    public class AxisPerformanceSnapshot
    {
        /// <summary>轴名称。</summary>
        public string AxisName { get; set; } = string.Empty;

        /// <summary>采样时间戳。</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>当前位置（mm）。</summary>
        public double Position { get; set; }

        /// <summary>目标位置（mm）。</summary>
        public double CommandPosition { get; set; }

        /// <summary>跟随误差 = 目标位置 - 实际位置（mm）。</summary>
        public double FollowingError { get; set; }

        /// <summary>当前速度（mm/s）。</summary>
        public double Velocity { get; set; }

        /// <summary>电机负载率（%，仿真值）。</summary>
        public double MotorLoad { get; set; }
    }

    /// <summary>
    /// 系统性能报告 — 30 轴系统的整体性能统计。
    /// </summary>
    public class SystemPerformanceReport
    {
        /// <summary>报告生成时间。</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        /// <summary>统计窗口（秒）。</summary>
        public double WindowSeconds { get; set; }

        /// <summary>总轴数。</summary>
        public int TotalAxes { get; set; }

        /// <summary>运动中的轴数。</summary>
        public int ActiveAxes { get; set; }

        /// <summary>报错的轴数。</summary>
        public int ErrorAxes { get; set; }

        /// <summary>最大跟随误差（mm，所有轴中最大值）。</summary>
        public double MaxFollowingError { get; set; }

        /// <summary>平均周期时间（ms）。</summary>
        public double AverageCycleTimeMs { get; set; }

        /// <summary>最大周期时间（ms）。</summary>
        public double MaxCycleTimeMs { get; set; }

        /// <summary>各轴性能快照。</summary>
        public List<AxisPerformanceSnapshot> AxisSnapshots { get; set; } = new();

        /// <summary>碰撞检测触发次数。</summary>
        public int CollisionWarnings { get; set; }
    }

    /// <summary>
    /// 通道同步屏障 — 多通道之间的同步点定义。
    /// 当一个通道到达同步点时，等待其他通道也到达后才继续。
    /// </summary>
    public class SyncBarrier
    {
        /// <summary>同步点编号。</summary>
        public int Id { get; set; }

        /// <summary>参与同步的通道编号列表。</summary>
        public List<int> ChannelIds { get; set; } = new();

        /// <summary>超时时间（毫秒，0=无超时）。</summary>
        public int TimeoutMs { get; set; } = 10000;
    }

    /// <summary>
    /// 龙门同步配置 — 两个平行轴同步运动（双驱龙门架构）。
    /// 主轴接收运动指令，从轴跟随主轴运动，系统监控两轴偏差。
    /// </summary>
    public class GantryConfig
    {
        /// <summary>主轴名称。</summary>
        public string MasterAxisName { get; set; } = string.Empty;

        /// <summary>从轴名称。</summary>
        public string SlaveAxisName { get; set; } = string.Empty;

        /// <summary>最大允许偏差（mm），超过此值触发报警。</summary>
        public double MaxDeviation { get; set; } = 0.05;

        /// <summary>偏差补偿增益（0~1，0=不补偿，1=全补偿）。</summary>
        public double CompensationGain { get; set; } = 0.5;
    }
}
