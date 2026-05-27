// ============================================================
// 文件：DomainModels.cs
// 用途：核心领域模型定义
// 设计思路：
//   定义整个系统共享的数据模型，这些模型是纯POCO对象（无依赖）。
//   所有层都可以引用 Domain 层，因此这些模型在系统中通用。
//   模型设计遵循不可变原则——配置类使用 init 属性，状态类使用 get/set。
// ============================================================

using SmartSemiCon.Domain.Enums;

namespace SmartSemiCon.Domain.Models
{
    /// <summary>
    /// 轴配置 — 定义一个运动轴的所有参数。
    /// 每个轴对应一个物理电机或虚拟轴，参数保存在Recipe中。
    /// </summary>
    public class AxisConfig
    {
        /// <summary>轴ID — 系统内唯一标识（0~N）</summary>
        public int AxisId { get; init; }

        /// <summary>轴名称 — 人类可读的标识（如 "X轴"、"Theta"）</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>所属控制卡ID — 标识该轴连接在哪张控制卡上</summary>
        public int CardId { get; init; }

        /// <summary>卡上轴号 — 该轴在控制卡上的物理编号</summary>
        public int CardAxisIndex { get; init; }

        /// <summary>脉冲当量 — 每移动1mm需要的脉冲数（pulse/mm）</summary>
        public double PulsePerUnit { get; set; } = 1000.0;

        /// <summary>最大速度 — 轴允许的最大运行速度（mm/s）</summary>
        public double MaxVelocity { get; set; } = 100.0;

        /// <summary>最大加速度 — 轴允许的最大加速度（mm/s²）</summary>
        public double MaxAcceleration { get; set; } = 500.0;

        /// <summary>最大减速度 — 轴允许的最大减速度（mm/s²）</summary>
        public double MaxDeceleration { get; set; } = 500.0;

        /// <summary>正方向软限位 — 正方向最大允许位置（mm）</summary>
        public double SoftLimitPositive { get; set; } = 1000.0;

        /// <summary>负方向软限位 — 负方向最大允许位置（mm）</summary>
        public double SoftLimitNegative { get; set; } = -1000.0;

        /// <summary>是否启用软限位保护</summary>
        public bool SoftLimitEnabled { get; set; } = true;

        /// <summary>回原点方向 — true为正方向回原，false为负方向</summary>
        public bool HomeDirection { get; set; } = false;

        /// <summary>回原点速度（mm/s）</summary>
        public double HomeVelocity { get; set; } = 10.0;

        /// <summary>回原点偏移量 — 找到原点后再移动的距离（mm）</summary>
        public double HomeOffset { get; set; } = 0.0;

        /// <summary>控制卡类型</summary>
        public MotionCardType CardType { get; init; } = MotionCardType.Simulation;
    }

    /// <summary>
    /// 轴实时状态 — 运动控制模块实时更新，UI层读取展示。
    /// 此对象为可变对象，由 AxisManager 定时刷新。
    /// </summary>
    public class AxisStatus
    {
        /// <summary>轴ID</summary>
        public int AxisId { get; init; }

        /// <summary>当前位置（mm）</summary>
        public double Position { get; set; }

        /// <summary>当前速度（mm/s）</summary>
        public double Velocity { get; set; }

        /// <summary>目标位置（mm）</summary>
        public double TargetPosition { get; set; }

        /// <summary>轴状态</summary>
        public AxisState State { get; set; } = AxisState.NotReady;

        /// <summary>是否已使能</summary>
        public bool IsServoOn { get; set; }

        /// <summary>是否在原点</summary>
        public bool IsHomed { get; set; }

        /// <summary>是否到达正限位</summary>
        public bool IsPositiveLimit { get; set; }

        /// <summary>是否到达负限位</summary>
        public bool IsNegativeLimit { get; set; }

        /// <summary>是否在运动中</summary>
        public bool IsMoving => State == AxisState.Moving || State == AxisState.Jogging;

        /// <summary>报警代码（0表示无报警）</summary>
        public int AlarmCode { get; set; }

        /// <summary>更新时间戳</summary>
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 点位数据 — 保存一个轴的目标位置和运动参数。
    /// 用于点位示教、Recipe中的位置表等场景。
    /// </summary>
    public class PointData
    {
        /// <summary>点位名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>目标位置（mm）</summary>
        public double Position { get; set; }

        /// <summary>运动速度（mm/s）</summary>
        public double Velocity { get; set; } = 50.0;

        /// <summary>加速度（mm/s²）</summary>
        public double Acceleration { get; set; } = 200.0;

        /// <summary>减速度（mm/s²）</summary>
        public double Deceleration { get; set; } = 200.0;
    }

    /// <summary>
    /// 报警记录 — 记录一次报警的完整信息。
    /// 持久化到数据库，用于报警历史查询和设备维护分析。
    /// </summary>
    public class AlarmRecord
    {
        /// <summary>报警ID — 数据库自增主键</summary>
        public long Id { get; set; }

        /// <summary>报警代码 — 全局唯一的报警标识（如 1001、2003）</summary>
        public int AlarmCode { get; set; }

        /// <summary>报警级别</summary>
        public AlarmLevel Level { get; set; }

        /// <summary>报警描述 — 人类可读的报警说明</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>报警来源 — 产生报警的模块名称（如 "运动控制"、"视觉"）</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>触发时间</summary>
        public DateTime OccurredAt { get; set; } = DateTime.Now;

        /// <summary>清除时间 — null表示尚未清除</summary>
        public DateTime? ClearedAt { get; set; }

        /// <summary>是否已清除</summary>
        public bool IsCleared => ClearedAt.HasValue;

        /// <summary>清除操作者</summary>
        public string? ClearedBy { get; set; }
    }

    /// <summary>
    /// 日志条目 — UI日志面板展示的日志数据。
    /// </summary>
    public class LogEntry
    {
        /// <summary>时间戳</summary>
        public DateTime Timestamp { get; init; } = DateTime.Now;

        /// <summary>日志级别</summary>
        public LogLevel Level { get; init; }

        /// <summary>日志模块来源</summary>
        public string Source { get; init; } = string.Empty;

        /// <summary>日志内容</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>追踪ID — 用于关联同一操作的多条日志</summary>
        public string? TraceId { get; init; }
    }

    /// <summary>
    /// 用户信息 — 操作员/工程师/管理员。
    /// </summary>
    public class UserInfo
    {
        /// <summary>用户ID</summary>
        public int Id { get; set; }

        /// <summary>用户名（登录账号）</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>显示名称</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>用户角色</summary>
        public UserRole Role { get; set; } = UserRole.Operator;

        /// <summary>密码哈希（SHA256）</summary>
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>是否启用</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>最后登录时间</summary>
        public DateTime? LastLoginAt { get; set; }
    }

    /// <summary>
    /// 配方数据 — 半导体设备的工艺参数集合。
    /// 包含设备运行所需的所有参数，切换配方即切换一整套工艺参数。
    /// </summary>
    public class RecipeData
    {
        /// <summary>配方ID</summary>
        public int Id { get; set; }

        /// <summary>配方名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>配方版本</summary>
        public string Version { get; set; } = "1.0";

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>修改时间</summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        /// <summary>创建者</summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>配方参数 — 键值对形式存储所有工艺参数</summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>点位表 — 轴ID到点位列表的映射</summary>
        public Dictionary<int, List<PointData>> PointTable { get; set; } = new();

        /// <summary>备注说明</summary>
        public string? Description { get; set; }
    }

    /// <summary>
    /// 相机配置 — 定义一台工业相机的参数。
    /// </summary>
    public class CameraConfig
    {
        /// <summary>相机ID</summary>
        public int CameraId { get; init; }

        /// <summary>相机名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>相机连接字符串（IP地址或序列号）</summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>图像宽度（像素）</summary>
        public int ImageWidth { get; set; } = 1920;

        /// <summary>图像高度（像素）</summary>
        public int ImageHeight { get; set; } = 1080;

        /// <summary>曝光时间（微秒）</summary>
        public double ExposureTime { get; set; } = 5000;

        /// <summary>增益</summary>
        public double Gain { get; set; } = 1.0;

        /// <summary>触发模式</summary>
        public TriggerMode TriggerMode { get; set; } = TriggerMode.Software;

        /// <summary>视觉引擎类型</summary>
        public VisionEngineType EngineType { get; set; } = VisionEngineType.Simulation;
    }

    /// <summary>
    /// 视觉检测结果 — 一次视觉处理的输出数据。
    /// </summary>
    public class VisionResult
    {
        /// <summary>检测是否成功</summary>
        public bool IsSuccess { get; set; }

        /// <summary>像素坐标X</summary>
        public double PixelX { get; set; }

        /// <summary>像素坐标Y</summary>
        public double PixelY { get; set; }

        /// <summary>角度（度）</summary>
        public double Angle { get; set; }

        /// <summary>匹配分数（0~1）</summary>
        public double Score { get; set; }

        /// <summary>世界坐标X（经标定转换后）</summary>
        public double WorldX { get; set; }

        /// <summary>世界坐标Y（经标定转换后）</summary>
        public double WorldY { get; set; }

        /// <summary>处理耗时（毫秒）</summary>
        public double ProcessTimeMs { get; set; }

        /// <summary>错误信息（失败时填充）</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>附加数据（算法特定的扩展数据）</summary>
        public Dictionary<string, object> ExtraData { get; set; } = new();
    }

    /// <summary>
    /// 标定数据 — 视觉坐标到运动坐标的转换参数。
    /// 通过9点标定法或仿射变换计算得出。
    /// </summary>
    public class CalibrationData
    {
        /// <summary>标定名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>关联相机ID</summary>
        public int CameraId { get; set; }

        /// <summary>仿射变换矩阵（3x3，行优先存储）</summary>
        public double[] TransformMatrix { get; set; } = new double[9];

        /// <summary>像素到毫米的比例（mm/pixel）</summary>
        public double PixelToMmRatio { get; set; } = 0.01;

        /// <summary>标定时间</summary>
        public DateTime CalibratedAt { get; set; } = DateTime.Now;

        /// <summary>标定精度（RMS误差，单位mm）</summary>
        public double RmsError { get; set; }

        /// <summary>标定点对数据（像素坐标 → 机械坐标）</summary>
        public List<CalibrationPoint> Points { get; set; } = new();
    }

    /// <summary>
    /// 标定点对 — 一组像素坐标和对应的机械坐标。
    /// </summary>
    public class CalibrationPoint
    {
        /// <summary>像素X</summary>
        public double PixelX { get; set; }

        /// <summary>像素Y</summary>
        public double PixelY { get; set; }

        /// <summary>机械X（mm）</summary>
        public double WorldX { get; set; }

        /// <summary>机械Y（mm）</summary>
        public double WorldY { get; set; }
    }

    /// <summary>
    /// 生产统计数据 — 记录生产批次的统计信息。
    /// </summary>
    public class ProductionRecord
    {
        /// <summary>记录ID</summary>
        public long Id { get; set; }

        /// <summary>批次号</summary>
        public string LotId { get; set; } = string.Empty;

        /// <summary>使用的配方名称</summary>
        public string RecipeName { get; set; } = string.Empty;

        /// <summary>开始时间</summary>
        public DateTime StartTime { get; set; }

        /// <summary>结束时间</summary>
        public DateTime? EndTime { get; set; }

        /// <summary>总数量</summary>
        public int TotalCount { get; set; }

        /// <summary>良品数量</summary>
        public int PassCount { get; set; }

        /// <summary>不良品数量</summary>
        public int FailCount { get; set; }

        /// <summary>良率（%）</summary>
        public double YieldRate => TotalCount > 0 ? (double)PassCount / TotalCount * 100.0 : 0;

        /// <summary>操作员</summary>
        public string OperatorName { get; set; } = string.Empty;
    }
}
