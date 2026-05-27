// ============================================================
// 文件：AxisController.cs
// 层级：硬件抽象层（Hardware Layer）> Motion > Drivers
// 职责：单轴控制器，封装对 IMotionCard 单轴操作，提供：
//       1. 轴状态机（Disabled→Enabled→Moving→Completed）
//       2. 软限位检查（在指令发出前验证目标位置合法性）
//       3. 运动参数校验（速度/加速度范围检查）
//       4. 到位判断（误差带 + 稳定时间，双重确认防止过冲误判）
//       5. 运动超时检测（超时后停止并发布错误事件）
//       6. 异步等待（MoveAbsoluteAsync 返回 Task，可 await）
//       7. 通过 IEventBus 发布运动完成/错误事件
//
// 设计思路：
//   AxisController 是 IMotionCard 的单轴视图，屏蔽了索引参数。
//   内部用 _positionMonitorTimer（20ms）轮询当前位置，判断是否到位。
//   状态机保证只有正确状态下才能发出运动指令（防止并发运动冲突）。
//
// IMotionCard 方法对应关系（新接口）：
//   EnableAxis(idx)              → 使能轴
//   DisableAxis(idx)             → 去使能轴
//   StartMove(idx,pos,vel,a,d)   → 绝对/相对运动
//   StopMove(idx)                → 减速停止
//   EmergencyStopAxis(idx)       → 急停
//   StartJog(idx,vel,acc)        → 点动
//   StartHoming(idx,config)      → 回零
//   ClearAxisError(idx)          → 清除错误
//   ReadPosition(idx)            → 读位置
//   ReadVelocity(idx)            → 读速度
//   ReadAxisStatus(idx)          → 读完整状态
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Models;
using SmartIndustry.Domain.Enums;
using SmartIndustry.Hardware;

namespace SmartIndustry.Hardware.Motion.Drivers
{
    /// <summary>
    /// 单轴控制器。
    /// 封装 IMotionCard 的单轴操作，是运动控制系统的核心单元。
    /// AxisManager 管理多个 AxisController 实例，每个轴一个。
    ///
    /// 状态机转换路径：
    ///   Disabled → (Enable) → Enabled
    ///   Enabled  → (Move)   → Moving
    ///   Moving   → (到位)   → Enabled
    ///   Moving   → (超时)   → Enabled + 发布错误事件
    ///   Enabled  → (Disable)→ Disabled
    ///   任何状态 → (Error)  → Error
    ///   Error    → (Reset)  → Enabled
    /// </summary>
    public class AxisController : IDisposable
    {
        // ==================== 私有字段 ====================

        /// <summary>底层运动控制卡接口</summary>
        private readonly IMotionCard _motionCard;

        /// <summary>事件总线（发布运动完成/错误事件）</summary>
        private readonly IEventBus _eventBus;

        /// <summary>该控制器管理的轴索引（在卡上的物理轴号）</summary>
        private readonly int _axisIndex;

        /// <summary>软限位配置</summary>
        private readonly SoftLimitConfig _softLimit;

        /// <summary>当前轴状态（volatile 保证多线程可见性）</summary>
        private volatile AxisState _currentState = AxisState.Disabled;

        /// <summary>位置监控定时器（20ms轮询）</summary>
        private Timer? _positionMonitorTimer;

        /// <summary>运动等待完成源（MoveAbsoluteAsync 内部使用）</summary>
        private TaskCompletionSource<bool>? _moveCompletionSource;

        /// <summary>运动开始时间（用于超时检测）</summary>
        private DateTime _moveStartTime;

        /// <summary>目标位置（运动指令发出时记录）</summary>
        private double _targetPosition;

        /// <summary>到位稳定计数器（连续N次在误差带内才判定到位）</summary>
        private int _inPositionCount;

        /// <summary>保护状态机和运动等待源的锁</summary>
        private readonly object _stateLock = new();

        // ==================== 配置常量 ====================

        /// <summary>到位误差带（mm，实际位置与目标位置之差小于此值视为到位）</summary>
        private const double InPositionBand = 0.01;

        /// <summary>连续到位确认次数（20ms × 5 = 100ms 稳定时间）</summary>
        private const int InPositionConfirmCount = 5;

        /// <summary>位置监控定时器周期（ms）</summary>
        private const int MonitorPeriodMs = 20;

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造单轴控制器
        /// </summary>
        /// <param name="motionCard">底层运动控制卡</param>
        /// <param name="axisIndex">轴索引（卡上物理轴号，0-based）</param>
        /// <param name="axisId">轴唯一标识字符串（如"X","Y1"）</param>
        /// <param name="eventBus">事件总线</param>
        /// <param name="softLimit">软限位配置（null=不启用软限位）</param>
        /// <param name="motionParams">默认运动参数</param>
        /// <param name="moveTimeoutMs">运动超时时间（ms，默认30000）</param>
        public AxisController(
            IMotionCard motionCard,
            int axisIndex,
            string axisId,
            IEventBus eventBus,
            SoftLimitConfig? softLimit = null,
            MotionParameters? motionParams = null,
            int moveTimeoutMs = 30000)
        {
            _motionCard = motionCard ?? throw new ArgumentNullException(nameof(motionCard));
            _axisIndex = axisIndex;
            AxisId = axisId;
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _softLimit = softLimit ?? new SoftLimitConfig { IsEnabled = false };
            DefaultMotionParams = motionParams ?? new MotionParameters();
            MoveTimeoutMs = moveTimeoutMs;
        }

        // ==================== 公开属性 ====================

        /// <summary>轴唯一标识（如"X","Y","Z"）</summary>
        public string AxisId { get; }

        /// <summary>当前轴状态（状态机当前节点）</summary>
        public AxisState CurrentState => _currentState;

        /// <summary>默认运动参数（可在运行时修改）</summary>
        public MotionParameters DefaultMotionParams { get; set; }

        /// <summary>运动超时时间（ms）</summary>
        public int MoveTimeoutMs { get; set; }

        // ==================== 轴使能控制 ====================

        /// <summary>
        /// 使能轴（Disabled → Enabled）
        /// </summary>
        /// <exception cref="InvalidOperationException">非 Disabled 状态时调用</exception>
        public void Enable()
        {
            lock (_stateLock)
            {
                if (_currentState != AxisState.Disabled)
                    throw new InvalidOperationException($"轴[{AxisId}]当前状态{_currentState}，无法使能");
                _motionCard.EnableAxis(_axisIndex);
                TransitionState(AxisState.Enabled);
            }
        }

        /// <summary>
        /// 去使能轴（Enabled → Disabled）
        /// </summary>
        public void Disable()
        {
            lock (_stateLock)
            {
                if (_currentState == AxisState.Moving)
                {
                    // 运动中去使能：先减速停止
                    _motionCard.StopMove(_axisIndex);
                    CancelMoveWait(false, "轴已去使能");
                }
                _motionCard.DisableAxis(_axisIndex);
                TransitionState(AxisState.Disabled);
            }
        }

        // ==================== 运动控制 ====================

        /// <summary>
        /// 发送绝对位置运动指令（异步等待到位）。
        /// 返回的 Task 在轴到达目标位置（或超时/错误）后完成。
        /// 内部调用 IMotionCard.StartMove(idx, pos, vel, accel, decel)。
        /// </summary>
        /// <param name="targetPosition">目标绝对位置（mm）</param>
        /// <param name="velocity">运动速度（mm/s，null=使用默认参数）</param>
        /// <param name="acceleration">加速度（mm/s²，null=使用默认参数）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>true=成功到位，false=超时/被取消/错误</returns>
        public Task<bool> MoveAbsoluteAsync(
            double targetPosition,
            double? velocity = null,
            double? acceleration = null,
            CancellationToken cancellationToken = default)
        {
            double v = velocity ?? DefaultMotionParams.MaxVelocity;
            double a = acceleration ?? DefaultMotionParams.Acceleration;
            // 减速度：优先使用 DefaultMotionParams.Deceleration，未设置则与加速度相同
            double dec = DefaultMotionParams.Deceleration > 0
                ? DefaultMotionParams.Deceleration
                : a;

            // 参数校验
            ValidateMotionParameters(targetPosition, v, a);

            lock (_stateLock)
            {
                // 状态检查：只有 Enabled 状态才能发出运动指令
                if (_currentState != AxisState.Enabled)
                    throw new InvalidOperationException($"轴[{AxisId}]当前状态{_currentState}，无法运动");

                // 软限位检查
                CheckSoftLimit(targetPosition);

                // 取消上一次未完成的等待
                CancelMoveWait(false, "新运动指令覆盖");

                // 创建新的等待源
                _moveCompletionSource = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _targetPosition = targetPosition;
                _moveStartTime = DateTime.Now;
                _inPositionCount = 0;

                // 调用新接口 StartMove（包含独立的加速度和减速度参数）
                _motionCard.StartMove(_axisIndex, targetPosition, v, a, dec);
                TransitionState(AxisState.Moving);

                // 启动位置监控定时器
                StartPositionMonitor();

                var tcs = _moveCompletionSource;

                // 注册取消回调
                cancellationToken.Register(() =>
                {
                    lock (_stateLock)
                    {
                        if (_currentState == AxisState.Moving)
                        {
                            _motionCard.StopMove(_axisIndex);
                            CancelMoveWait(false, "运动被取消");
                            TransitionState(AxisState.Enabled);
                        }
                    }
                });

                return tcs.Task;
            }
        }

        /// <summary>
        /// 发送绝对位置运动指令（异步等待到位，使用运动参数对象）
        /// </summary>
        public Task<bool> MoveAbsoluteAsync(
            double targetPosition,
            MotionParameters parameters,
            CancellationToken cancellationToken = default)
            => MoveAbsoluteAsync(targetPosition, parameters.MaxVelocity, parameters.Acceleration, cancellationToken);

        /// <summary>
        /// 发送相对位置运动指令（异步等待到位）。
        /// 内部将相对位移转换为绝对位置后调用 MoveAbsoluteAsync。
        /// </summary>
        public Task<bool> MoveRelativeAsync(
            double distance,
            double? velocity = null,
            double? acceleration = null,
            CancellationToken cancellationToken = default)
        {
            // 读取当前实际位置作为基准
            double currentPos = _motionCard.ReadPosition(_axisIndex);
            return MoveAbsoluteAsync(currentPos + distance, velocity, acceleration, cancellationToken);
        }

        /// <summary>
        /// 启动点动（不等待完成）。
        /// 点动持续运动直到调用 Stop()。
        /// </summary>
        /// <param name="velocity">正值=正向，负值=负向（mm/s）</param>
        public void StartJog(double velocity)
        {
            lock (_stateLock)
            {
                if (_currentState != AxisState.Enabled)
                    throw new InvalidOperationException($"轴[{AxisId}]当前状态{_currentState}，无法点动");

                double a = DefaultMotionParams.Acceleration;
                _motionCard.StartJog(_axisIndex, velocity, a);
                TransitionState(AxisState.Moving);
            }
        }

        /// <summary>
        /// 停止运动（减速停止）
        /// </summary>
        public void Stop()
        {
            lock (_stateLock)
            {
                _motionCard.StopMove(_axisIndex);
                CancelMoveWait(true, "轴已停止");
                if (_currentState == AxisState.Moving)
                    TransitionState(AxisState.Enabled);
            }
        }

        /// <summary>
        /// 急停（立即切断脉冲，不减速）
        /// </summary>
        public void EmergencyStop()
        {
            lock (_stateLock)
            {
                _motionCard.EmergencyStopAxis(_axisIndex);
                CancelMoveWait(false, "急停触发");
                TransitionState(AxisState.Error);
            }
        }

        // ==================== 回零控制 ====================

        /// <summary>
        /// 异步回零（等待回零完成）。
        /// 内部调用 IMotionCard.StartHoming(idx, config)，
        /// 由位置监控定时器检测 IsHomed 标志确认完成。
        /// </summary>
        public async Task<bool> HomeAsync(HomingConfig? config = null, CancellationToken cancellationToken = default)
        {
            var homingConfig = config ?? new HomingConfig();
            lock (_stateLock)
            {
                if (_currentState != AxisState.Enabled)
                    throw new InvalidOperationException($"轴[{AxisId}]当前状态{_currentState}，无法回零");

                _moveCompletionSource = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _targetPosition = homingConfig.Offset; // 回零目标=偏移位置
                _moveStartTime = DateTime.Now;
                _inPositionCount = 0;

                _motionCard.StartHoming(_axisIndex, homingConfig);
                TransitionState(AxisState.Homing);
                StartPositionMonitor();
            }

            bool result = await _moveCompletionSource!.Task;
            return result;
        }

        // ==================== 状态查询 ====================

        /// <summary>
        /// 获取当前实际位置（mm），调用 IMotionCard.ReadPosition
        /// </summary>
        public double GetActualPosition() => _motionCard.ReadPosition(_axisIndex);

        /// <summary>
        /// 获取当前速度（mm/s），调用 IMotionCard.ReadVelocity
        /// </summary>
        public double GetActualVelocity() => _motionCard.ReadVelocity(_axisIndex);

        /// <summary>
        /// 获取完整轴状态快照，调用 IMotionCard.ReadAxisStatus
        /// </summary>
        public AxisStatus GetStatus() => _motionCard.ReadAxisStatus(_axisIndex);

        /// <summary>
        /// 清除错误，恢复到 Enabled 状态。
        /// 调用 IMotionCard.ClearAxisError 清除硬件错误标志，然后更新状态机。
        /// </summary>
        public void ClearError()
        {
            lock (_stateLock)
            {
                _motionCard.ClearAxisError(_axisIndex);
                if (_currentState == AxisState.Error)
                    TransitionState(AxisState.Enabled);
            }
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 状态转换（触发状态变化事件）。
        /// 使用 Fire-and-Forget 方式发布事件，避免在锁中 await 导致死锁。
        /// </summary>
        private void TransitionState(AxisState newState)
        {
            var oldState = _currentState;
            _currentState = newState;
            // 发布状态变化事件（通过 IEventBus 解耦）
            if (oldState != newState)
            {
                // 使用 Fire-and-forget 方式发布（避免在锁中 await）
                _ = _eventBus.PublishAsync(new HardwareAxisStateChangedEvent(AxisId, oldState, newState, DateTime.Now));
            }
        }

        /// <summary>
        /// 启动位置监控定时器（停止旧定时器，启动新定时器）
        /// </summary>
        private void StartPositionMonitor()
        {
            // 先停止旧定时器
            _positionMonitorTimer?.Dispose();
            _positionMonitorTimer = new Timer(PositionMonitorCallback, null,
                MonitorPeriodMs, MonitorPeriodMs);
        }

        /// <summary>
        /// 位置监控定时器回调（20ms周期）。
        /// 检查：
        ///   1. 超时：运动时间超过 MoveTimeoutMs → 停止并发布错误
        ///   2. 到位：连续 InPositionConfirmCount 次位置误差 &lt; InPositionBand → 完成
        ///   3. 限位触发：检测到限位信号 → 停止并发布错误
        ///   4. 回零完成：IsHomed 为 true 且状态为 Homing → 完成
        /// </summary>
        private void PositionMonitorCallback(object? state)
        {
            lock (_stateLock)
            {
                if (_currentState != AxisState.Moving && _currentState != AxisState.Homing) return;

                // 调用新接口 ReadAxisStatus（原 GetAxisStatus）
                var axisStatus = _motionCard.ReadAxisStatus(_axisIndex);

                // ---- 超时检测 ----
                if ((DateTime.Now - _moveStartTime).TotalMilliseconds > MoveTimeoutMs)
                {
                    _motionCard.StopMove(_axisIndex);
                    StopMonitorAndTransition(false, "运动超时");
                    return;
                }

                // ---- 限位触发检测 ----
                if (axisStatus.PositiveLimitActive || axisStatus.NegativeLimitActive)
                {
                    StopMonitorAndTransition(false,
                        axisStatus.PositiveLimitActive ? "正限位触发" : "负限位触发");
                    return;
                }

                // ---- 回零完成检测 ----
                if (_currentState == AxisState.Homing && axisStatus.IsHomed)
                {
                    StopMonitorAndTransition(true, null);
                    return;
                }

                // ---- 到位检测（仅运动模式）----
                if (_currentState == AxisState.Moving)
                {
                    double posError = Math.Abs(axisStatus.ActualPosition - _targetPosition);
                    if (posError <= InPositionBand && !axisStatus.IsMoving)
                    {
                        _inPositionCount++;
                        if (_inPositionCount >= InPositionConfirmCount)
                        {
                            // 连续 N 次在误差带内，确认到位
                            StopMonitorAndTransition(true, null);
                        }
                    }
                    else
                    {
                        // 不在误差带内，重置计数
                        _inPositionCount = 0;
                    }
                }
            }
        }

        /// <summary>
        /// 停止位置监控，完成等待任务，转换状态，并发布运动完成/错误事件
        /// </summary>
        private void StopMonitorAndTransition(bool success, string? errorMessage)
        {
            _positionMonitorTimer?.Dispose();
            _positionMonitorTimer = null;

            // 调用新接口 ReadAxisStatus（原 GetAxisStatus）
            var axisStatus = _motionCard.ReadAxisStatus(_axisIndex);
            double actualPos = axisStatus.ActualPosition;
            double posError = Math.Abs(actualPos - _targetPosition);
            var duration = DateTime.Now - _moveStartTime;

            TransitionState(success ? AxisState.Enabled : AxisState.Error);

            // 发布运动完成事件
            _ = _eventBus.PublishAsync(new HardwareMotionCompletedEvent(
                AxisId, _targetPosition, actualPos, posError, duration, success));

            // 若失败，发布错误事件
            if (!success && errorMessage != null)
            {
                _ = _eventBus.PublishAsync(new HardwareAxisErrorEvent(
                    AxisId, 1, errorMessage, DateTime.Now));
            }

            CancelMoveWait(success, errorMessage);
        }

        /// <summary>
        /// 完成或取消当前运动等待 Task
        /// </summary>
        private void CancelMoveWait(bool result, string? reason)
        {
            if (_moveCompletionSource == null) return;
            var tcs = _moveCompletionSource;
            _moveCompletionSource = null;
            // 在线程池上完成 Task，避免死锁
            Task.Run(() => tcs.TrySetResult(result));
        }

        /// <summary>
        /// 软限位检查（仅在 IsEnabled=true 时检查）
        /// </summary>
        private void CheckSoftLimit(double targetPosition)
        {
            if (!_softLimit.IsEnabled) return;
            if (targetPosition > _softLimit.PositiveLimit)
                throw new ArgumentOutOfRangeException(nameof(targetPosition),
                    $"目标位置{targetPosition:F3}超过正向软限位{_softLimit.PositiveLimit:F3}");
            if (targetPosition < _softLimit.NegativeLimit)
                throw new ArgumentOutOfRangeException(nameof(targetPosition),
                    $"目标位置{targetPosition:F3}超过负向软限位{_softLimit.NegativeLimit:F3}");
        }

        /// <summary>
        /// 运动参数校验（防止速度/加速度为零或负值）
        /// </summary>
        private static void ValidateMotionParameters(double targetPosition, double velocity, double acceleration)
        {
            if (double.IsNaN(targetPosition) || double.IsInfinity(targetPosition))
                throw new ArgumentException("目标位置无效（NaN或Infinity）");
            if (velocity <= 0)
                throw new ArgumentException($"速度必须大于0，当前值：{velocity}");
            if (acceleration <= 0)
                throw new ArgumentException($"加速度必须大于0，当前值：{acceleration}");
        }

        // ==================== IDisposable ====================

        /// <inheritdoc/>
        public void Dispose()
        {
            _positionMonitorTimer?.Dispose();
            _moveCompletionSource?.TrySetResult(false);
        }
    }
}
