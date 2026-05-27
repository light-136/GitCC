// ============================================================
// 文件：DomainEvent.cs
// 层次：领域层 (Domain Layer) — 领域事件
// 职责：
//   1. 定义领域事件的抽象基类，携带事件身份和时间戳
//   2. 定义平台内所有具体领域事件类（共 18 个）
// 设计思路：
//   领域事件是 DDD 战术设计的核心工具。实体状态发生重要变化时，
//   不直接调用其他模块，而是发布事件，由事件总线异步通知订阅方。
//   这样可以彻底解耦运动控制、视觉、通信、报警等模块之间的依赖，
//   使得每个模块可以独立演化。
//
//   事件命名规范：[主语][动词过去式]Event
//   例：DeviceStateChangedEvent = 设备状态已变更事件
//
//   所有事件类使用 record 定义，保证不可变性（天然线程安全），
//   并自动获得结构化相等比较能力。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.ValueObjects;

namespace SmartIndustry.Domain.Events
{
    // ====================================================================
    // 领域事件抽象基类
    // ====================================================================

    /// <summary>
    /// 领域事件基类。
    /// 所有领域事件必须继承此类，事件总线依赖此类型进行泛型分发。
    /// 使用 abstract record 确保：
    ///   1. 事件不可变（记录类型的属性 init-only）
    ///   2. 值相等性比较（两个内容相同的事件被视为相同事件）
    ///   3. 位置参数构造（子类初始化简洁）
    /// </summary>
    public abstract record DomainEvent
    {
        /// <summary>
        /// 事件唯一标识符（UUID v4）。
        /// 用于幂等处理：消费方可根据 EventId 判断是否已处理过该事件。
        /// </summary>
        public Guid EventId { get; init; } = Guid.NewGuid();

        /// <summary>
        /// 事件发生时间（UTC）。
        /// 记录事件产生的精确时间，用于事件溯源和因果链分析。
        /// </summary>
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// 事件类型名称（完全限定类型名的简写）。
        /// 用于事件总线路由和序列化/反序列化的类型标识。
        /// </summary>
        public string EventType => GetType().Name;
    }

    // ====================================================================
    // 设备相关领域事件
    // ====================================================================

    /// <summary>
    /// 设备状态变更事件。
    /// 当设备从一个状态转换到另一个状态时触发。
    /// 订阅方：UI 仪表板（更新状态图标）、报警模块（Faulted 状态生成报警）、
    ///         日志模块（记录状态变化历史）。
    /// </summary>
    /// <param name="OldState">变更前的状态</param>
    /// <param name="NewState">变更后的状态</param>
    /// <param name="Reason">状态变更的原因说明（可为 null）</param>
    public record DeviceStateChangedEvent(
        DeviceState OldState,
        DeviceState NewState,
        string? Reason = null
    ) : DomainEvent;

    // ====================================================================
    // 运动轴相关领域事件
    // ====================================================================

    /// <summary>
    /// 轴运动启动事件。
    /// 运动指令下发并被运动控制器接受时触发。
    /// 订阅方：UI 进度条（显示运动进行中）、日志模块（记录运动指令）。
    /// </summary>
    /// <param name="AxisId">轴的唯一标识符</param>
    /// <param name="TargetPosition">目标位置（三维坐标）</param>
    /// <param name="Profile">运动参数（速度/加速度/减速度）</param>
    public record AxisMotionStartedEvent(
        Guid AxisId,
        Position3D TargetPosition,
        MotionProfile Profile
    ) : DomainEvent;

    /// <summary>
    /// 轴运动完成事件。
    /// 轴到达目标位置（在定位窗口内）并完成稳定后触发。
    /// 订阅方：流程协调模块（触发下一个工序步骤）、统计模块（记录节拍时间）。
    /// </summary>
    /// <param name="AxisId">轴的唯一标识符</param>
    /// <param name="ActualPosition">实际到达位置（三维坐标）</param>
    /// <param name="ElapsedMs">运动耗时（毫秒）</param>
    public record AxisMotionCompletedEvent(
        Guid AxisId,
        Position3D ActualPosition,
        long ElapsedMs
    ) : DomainEvent;

    /// <summary>
    /// 轴错误事件。
    /// 轴发生错误（超差、过载、编码器故障、限位等）时触发。
    /// 订阅方：报警模块（生成 Motion 类别报警）、安全模块（可能触发急停）。
    /// </summary>
    /// <param name="AxisId">发生错误的轴标识符</param>
    /// <param name="ErrorCode">错误代码（运动控制卡定义的错误码）</param>
    /// <param name="Message">错误描述信息</param>
    public record AxisErrorEvent(
        Guid AxisId,
        int ErrorCode,
        string Message
    ) : DomainEvent;

    // ====================================================================
    // 报警相关领域事件
    // ====================================================================

    /// <summary>
    /// 报警触发事件。
    /// 新报警发生时触发，携带完整的报警记录实体引用。
    /// 订阅方：UI 报警列表（新增报警条目）、声光报警器（驱动硬件输出）、
    ///         邮件/SMS 通知模块（高级别报警远程通知）。
    /// </summary>
    /// <param name="AlarmRecord">触发的报警记录实体</param>
    public record AlarmTriggeredEvent(
        Entities.AlarmRecord AlarmRecord
    ) : DomainEvent;

    /// <summary>
    /// 报警确认事件。
    /// 操作员确认（Acknowledge）一条活动报警时触发。
    /// 订阅方：UI 报警列表（更新确认状态）、审计日志模块。
    /// </summary>
    /// <param name="AlarmCode">被确认的报警编码</param>
    /// <param name="AcknowledgedBy">确认操作人用户名</param>
    public record AlarmAcknowledgedEvent(
        string AlarmCode,
        string AcknowledgedBy
    ) : DomainEvent;

    // ====================================================================
    // 视觉相关领域事件
    // ====================================================================

    /// <summary>
    /// 视觉任务完成事件。
    /// 视觉引擎执行完一次视觉检测任务后触发，无论结果 Pass/Fail。
    /// 订阅方：流程协调模块（Pass 则继续，Fail 则触发剔除或停机）、
    ///         统计模块（累计良率数据）、UI 图像显示（刷新结果图像）。
    /// </summary>
    /// <param name="TaskId">视觉任务的唯一标识符</param>
    /// <param name="IsPass">检测是否通过</param>
    /// <param name="Score">匹配或置信度得分（0.0 ~ 1.0）</param>
    /// <param name="ResultImagePath">标注结果图像的存储路径（可为 null）</param>
    public record VisionTaskCompletedEvent(
        Guid TaskId,
        bool IsPass,
        double Score,
        string? ResultImagePath = null
    ) : DomainEvent;

    // ====================================================================
    // 通信相关领域事件
    // ====================================================================

    /// <summary>
    /// 通信通道连接状态变更事件。
    /// 通道连接成功、断开或发生错误时触发。
    /// 订阅方：UI 通信状态面板（显示连接图标）、报警模块（断连时生成通信报警）、
    ///         自动重连服务（Error 状态触发重连计时器）。
    /// </summary>
    /// <param name="ChannelName">通信通道名称</param>
    /// <param name="OldState">变更前的连接状态</param>
    /// <param name="NewState">变更后的连接状态</param>
    public record CommunicationStateChangedEvent(
        string ChannelName,
        ConnectionState OldState,
        ConnectionState NewState
    ) : DomainEvent;

    /// <summary>
    /// 数据接收事件。
    /// 通信通道收到完整数据帧时触发（已完成粘包处理）。
    /// 订阅方：协议解析模块（按协议格式解析字节数组）、
    ///         数据记录模块（原始数据归档）。
    /// </summary>
    /// <param name="ChannelName">接收数据的通道名称</param>
    /// <param name="Data">接收到的原始字节数据</param>
    public record DataReceivedEvent(
        string ChannelName,
        byte[] Data
    ) : DomainEvent;

    // ====================================================================
    // 用户相关领域事件
    // ====================================================================

    /// <summary>
    /// 用户登录事件。
    /// 用户认证成功并建立会话时触发。
    /// 订阅方：审计日志模块（记录登录日志）、协同模块（通知在线用户列表变更）。
    /// </summary>
    /// <param name="Username">登录的用户名</param>
    /// <param name="Role">用户的角色</param>
    /// <param name="LoginAt">登录时间（UTC）</param>
    public record UserLoginEvent(
        string Username,
        UserRole Role,
        DateTime LoginAt
    ) : DomainEvent;

    /// <summary>
    /// 用户登出事件。
    /// 用户主动登出或会话超时时触发。
    /// 订阅方：审计日志模块、协同模块（清理该用户的协同会话）。
    /// </summary>
    /// <param name="Username">登出的用户名</param>
    /// <param name="LogoutAt">登出时间（UTC）</param>
    public record UserLogoutEvent(
        string Username,
        DateTime LogoutAt
    ) : DomainEvent;

    // ====================================================================
    // 配方相关领域事件
    // ====================================================================

    /// <summary>
    /// 配方变更事件。
    /// 配方被创建、修改或切换激活版本时触发。
    /// 订阅方：运动控制模块（加载新运动参数）、视觉模块（加载新检测参数）、
    ///         审计日志模块（追踪配方变更历史）。
    /// </summary>
    /// <param name="RecipeName">配方名称</param>
    /// <param name="OldVersion">变更前的版本号（首次创建时为 null）</param>
    /// <param name="NewVersion">变更后的版本号</param>
    public record RecipeChangedEvent(
        string RecipeName,
        int? OldVersion,
        int NewVersion
    ) : DomainEvent;

    // ====================================================================
    // 协同会话相关领域事件
    // ====================================================================

    /// <summary>
    /// 协同用户加入事件。
    /// 用户成功加入一个多人协同会话时触发。
    /// 订阅方：UI 协同面板（更新在线成员列表）、会话所有者（收到加入通知）。
    /// </summary>
    /// <param name="SessionId">协同会话标识符</param>
    /// <param name="UserId">加入的用户标识符</param>
    /// <param name="Role">用户在会话中的角色</param>
    public record CollaborationUserJoinedEvent(
        Guid SessionId,
        Guid UserId,
        CollaborationRole Role
    ) : DomainEvent;

    /// <summary>
    /// 协同用户离开事件。
    /// 用户主动退出或超时断线时触发。
    /// 订阅方：UI 协同面板（从成员列表移除）、会话管理（若所有者离开则转移或关闭会话）。
    /// </summary>
    /// <param name="SessionId">协同会话标识符</param>
    /// <param name="UserId">离开的用户标识符</param>
    public record CollaborationUserLeftEvent(
        Guid SessionId,
        Guid UserId
    ) : DomainEvent;

    /// <summary>
    /// 协同状态更新事件。
    /// 编辑者修改会话共享状态中的某个键值对时触发。
    /// 订阅方：所有会话成员的 UI（实时同步状态变化）。
    /// </summary>
    /// <param name="SessionId">协同会话标识符</param>
    /// <param name="Key">变更的状态键名</param>
    /// <param name="Value">变更后的状态值（序列化为字符串）</param>
    public record CollaborationStateUpdatedEvent(
        Guid SessionId,
        string Key,
        string Value
    ) : DomainEvent;

    // ====================================================================
    // 文件操作相关领域事件
    // ====================================================================

    /// <summary>
    /// 文件操作事件。
    /// 文件服务执行读写/复制/移动/删除/压缩等操作完成时触发。
    /// 订阅方：审计日志模块（记录文件操作历史）、UI 文件浏览器（刷新目录）。
    /// </summary>
    /// <param name="OperationType">操作类型</param>
    /// <param name="FilePath">操作的文件路径</param>
    /// <param name="Success">操作是否成功</param>
    public record FileOperationEvent(
        FileOperationType OperationType,
        string FilePath,
        bool Success
    ) : DomainEvent;

    // ====================================================================
    // 数据库操作相关领域事件
    // ====================================================================

    /// <summary>
    /// 数据库查询事件（性能监控用途）。
    /// 执行时间超过阈值的查询完成后触发，用于慢查询分析和优化。
    /// 订阅方：性能监控模块、DBA 告警通道。
    /// </summary>
    /// <param name="Query">执行的 SQL 或查询描述</param>
    /// <param name="Duration">查询耗时</param>
    /// <param name="RowCount">返回的数据行数</param>
    public record DatabaseQueryEvent(
        string Query,
        TimeSpan Duration,
        int RowCount
    ) : DomainEvent;

    // ====================================================================
    // 系统级领域事件
    // ====================================================================

    /// <summary>
    /// 系统关机事件。
    /// 平台收到关机指令并开始执行有序关机序列时触发（最高优先级事件）。
    /// 订阅方：所有模块（执行各自的清理/保存/复位操作）。
    /// 设计说明：关机事件应同步分发，确保所有模块在进程退出前完成清理。
    /// </summary>
    /// <param name="Reason">关机原因（如"用户请求"、"系统更新"、"紧急关机"）</param>
    public record SystemShutdownEvent(
        string Reason
    ) : DomainEvent;
}
