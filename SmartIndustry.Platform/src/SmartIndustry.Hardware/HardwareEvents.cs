// ============================================================
// 文件：HardwareEvents.cs
// 层级：硬件抽象层（Hardware Layer）
// 职责：定义硬件抽象层的所有领域事件。
//       这些事件继承自 Domain.Events.DomainEvent，
//       可通过 IEventBus 发布和订阅。
//
// 事件分类：
//   - 运动控制事件（轴状态变化、运动完成、运动错误）
//   - 视觉事件（视觉结果、对位完成）
//   - IO事件（数字IO变化）
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Events;
using SmartIndustry.Domain.Models;
using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Hardware
{
    // ============================================================
    // 运动控制相关事件
    // ============================================================

    /// <summary>
    /// 轴状态变化事件 — 轴控制器状态机发生状态转换时发布。
    /// 订阅方：UI层（刷新轴状态显示）、日志模块（记录状态历史）。
    /// </summary>
    public record HardwareAxisStateChangedEvent(
        string AxisId,
        AxisState OldState,
        AxisState NewState,
        DateTime Timestamp
    ) : DomainEvent;

    /// <summary>
    /// 运动完成事件 — 轴到达目标位置（或失败）时发布。
    /// 订阅方：流程协调模块（触发下一步）、统计模块（记录运动时间）。
    /// </summary>
    public record HardwareMotionCompletedEvent(
        string AxisId,
        double TargetPosition,
        double ActualPosition,
        double PositionError,
        TimeSpan Duration,
        bool IsSuccess
    ) : DomainEvent;

    /// <summary>
    /// 轴错误事件 — 轴发生错误（超时/限位/驱动故障）时发布。
    /// 订阅方：报警模块（生成 Motion 类别报警）。
    /// </summary>
    public record HardwareAxisErrorEvent(
        string AxisId,
        int ErrorCode,
        string ErrorMessage,
        DateTime Timestamp
    ) : DomainEvent;

    // ============================================================
    // IO相关事件
    // ============================================================

    /// <summary>
    /// IO通道状态变化事件 — 数字IO边沿触发（消抖后）时发布。
    /// 订阅方：流程协调模块（IO触发联锁）、UI层（IO监控显示）。
    /// </summary>
    public record HardwareIoChangedEvent(
        string DeviceId,
        IoChannel Channel,
        string ChangeType
    ) : DomainEvent;

    // ============================================================
    // 视觉相关事件
    // ============================================================

    /// <summary>
    /// 视觉结果事件 — 视觉算法执行完成时发布。
    /// 订阅方：流程协调模块（Pass/NG判断）、统计模块（良率累计）。
    /// </summary>
    public record HardwareVisionResultEvent(
        string EngineId,
        string TaskType,
        bool IsSuccess,
        double Score
    ) : DomainEvent;

    /// <summary>
    /// 视觉-运动对位完成事件 — VisionMotionCoordinator 完成对位补偿时发布。
    /// </summary>
    public record HardwareVisionMotionAlignedEvent(
        string EngineId,
        double OffsetX,
        double OffsetY,
        double MatchScore
    ) : DomainEvent;
}
