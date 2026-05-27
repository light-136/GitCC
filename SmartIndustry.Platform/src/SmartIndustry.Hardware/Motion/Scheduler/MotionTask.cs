// ============================================================
// 文件：MotionTask.cs
// 层级：硬件抽象层（Hardware Layer）> Motion > Scheduler
// 职责：定义运动任务的数据结构，是运动调度器的基本工作单元。
//       包含任务元数据、运动参数、状态流转和完成回调。
//
// 设计思路：
//   MotionTask 采用不可变设计（创建后参数不变，只有 Status 可变），
//   防止任务在执行过程中被外部修改导致数据竞争。
//   完成回调（OnCompleted）在任务执行完成时被调用，支持结果传递。
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Models;
using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Hardware.Motion.Scheduler
{
    /// <summary>
    /// 运动任务优先级枚举（数值越小优先级越高）
    /// </summary>
    public enum MotionTaskPriority
    {
        /// <summary>实时优先级（急停、安全互锁等，专用线程执行）</summary>
        RealTime = 0,

        /// <summary>高优先级（视觉引导运动、精确定位等时间敏感任务）</summary>
        High = 1,

        /// <summary>普通优先级（常规位置移动、换轨等）</summary>
        Normal = 2,

        /// <summary>后台优先级（预热运动、位置回调等低优先级任务）</summary>
        Background = 3
    }

    /// <summary>
    /// 运动任务状态枚举（任务生命周期的各阶段）
    /// </summary>
    public enum MotionTaskStatus
    {
        /// <summary>待执行（已提交到队列，等待调度）</summary>
        Pending = 0,

        /// <summary>执行中（已出队，正在运动）</summary>
        Running = 1,

        /// <summary>已完成（运动成功到位）</summary>
        Completed = 2,

        /// <summary>已取消（调用 Cancel 或 CancellationToken 触发）</summary>
        Cancelled = 3,

        /// <summary>执行失败（超时/限位/硬件错误）</summary>
        Error = 4
    }

    /// <summary>
    /// 运动任务执行结果 — 传递给完成回调的数据。
    /// </summary>
    public class MotionTaskResult
    {
        /// <summary>关联的任务</summary>
        public MotionTask Task { get; set; } = null!;

        /// <summary>执行是否成功</summary>
        public bool IsSuccess { get; set; }

        /// <summary>实际到达位置（mm）</summary>
        public double ActualPosition { get; set; }

        /// <summary>定位误差（mm）</summary>
        public double PositionError { get; set; }

        /// <summary>执行耗时</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>失败原因（成功时为空）</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>任务完成时间戳</summary>
        public DateTime CompletedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 运动任务 — 描述一次完整的轴运动请求。
    /// 由外部代码创建并提交给 MotionScheduler，调度器负责排队和执行。
    ///
    /// 使用示例：
    ///   var task = new MotionTask
    ///   {
    ///       AxisId = "X",
    ///       Priority = MotionTaskPriority.Normal,
    ///       MotionMode = MotionMode.Absolute,
    ///       TargetPosition = 100.0,
    ///       Profile = new MotionParameters { MaxVelocity = 200 },
    ///       OnCompleted = result => Console.WriteLine(result.IsSuccess)
    ///   };
    ///   scheduler.Enqueue(task);
    /// </summary>
    public class MotionTask
    {
        // ==================== 任务身份 ====================

        /// <summary>任务唯一ID（GUID字符串，自动生成）</summary>
        public string TaskId { get; init; } = Guid.NewGuid().ToString("N");

        /// <summary>任务名称（可选，用于日志）</summary>
        public string TaskName { get; set; } = string.Empty;

        // ==================== 调度参数 ====================

        /// <summary>任务优先级</summary>
        public MotionTaskPriority Priority { get; init; } = MotionTaskPriority.Normal;

        /// <summary>创建时间戳（调度器用于 FIFO 排序）</summary>
        public DateTime CreatedAt { get; } = DateTime.Now;

        // ==================== 运动参数 ====================

        /// <summary>目标轴标识（对应 AxisController 的 AxisId）</summary>
        public string AxisId { get; init; } = string.Empty;

        /// <summary>运动模式（绝对/相对/点动/回零）</summary>
        public MotionMode MotionMode { get; init; } = MotionMode.Absolute;
        /// <summary>目标位置（mm，MotionMode=Absolute/Relative 时有效）</summary>
        public double TargetPosition { get; init; }

        /// <summary>运动参数（速度、加速度、减速度、Jerk）</summary>
        public MotionParameters Profile { get; init; } = new();

        /// <summary>回零配置（MotionMode=Home 时有效）</summary>
        public HomingConfig? HomingConfig { get; init; }

        // ==================== 状态（可变）====================

        /// <summary>任务当前状态（由调度器更新，外部只读）</summary>
        public MotionTaskStatus Status { get; internal set; } = MotionTaskStatus.Pending;

        /// <summary>执行开始时间</summary>
        public DateTime? StartedAt { get; internal set; }

        /// <summary>执行完成时间</summary>
        public DateTime? FinishedAt { get; internal set; }

        // ==================== 回调 ====================

        /// <summary>
        /// 完成回调（任务完成/失败/取消时调用，在调度器线程上执行）。
        /// 注意：回调代码应快速返回，耗时操作须另起线程。
        /// </summary>
        public Action<MotionTaskResult>? OnCompleted { get; set; }

        // ==================== 辅助属性 ====================

        /// <summary>任务总耗时（从创建到完成）</summary>
        public TimeSpan TotalElapsed =>
            FinishedAt.HasValue ? FinishedAt.Value - CreatedAt : TimeSpan.Zero;

        /// <summary>任务执行耗时（从开始执行到完成）</summary>
        public TimeSpan ExecutionElapsed =>
            (StartedAt.HasValue && FinishedAt.HasValue)
                ? FinishedAt.Value - StartedAt.Value
                : TimeSpan.Zero;

        /// <summary>返回任务的简短描述（用于日志）</summary>
        public override string ToString()
            => $"[{Priority}] Task={TaskId[..8]} Axis={AxisId} Mode={MotionMode} Target={TargetPosition:F3}mm Status={Status}";
    }
}
