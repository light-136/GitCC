// ============================================================
// 文件：MotionScheduler.cs
// 用途：运动任务调度器
// 设计思路：
//   工业设备中，运动指令不是简单地一个接一个执行。
//   需要一个调度器来管理运动任务的优先级、队列和并发执行。
//
//   核心概念：
//   - MotionTask：一个运动任务（可包含多轴的协调运动）
//   - CommandQueue：先进先出的命令队列
//   - MotionScheduler：从队列取出任务，分配给轴执行
//
//   调度器使用生产者-消费者模式：
//   - 生产者：流程代码向队列提交运动任务
//   - 消费者：调度器线程从队列取出任务并执行
// ============================================================

using System.Collections.Concurrent;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Hardware.Motion.Axis;

namespace SmartSemiCon.Hardware.Motion.Scheduler
{
    /// <summary>
    /// 运动任务 — 描述一个运动指令。
    /// </summary>
    public class MotionTask
    {
        /// <summary>任务ID</summary>
        public int TaskId { get; init; }

        /// <summary>任务名称</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>运动模式</summary>
        public MotionMode Mode { get; init; }

        /// <summary>涉及的轴ID列表</summary>
        public int[] AxisIds { get; init; } = Array.Empty<int>();

        /// <summary>目标位置</summary>
        public double[] Positions { get; init; } = Array.Empty<double>();

        /// <summary>运动速度</summary>
        public double Velocity { get; init; }

        /// <summary>加速度</summary>
        public double Acceleration { get; init; }

        /// <summary>减速度</summary>
        public double Deceleration { get; init; }

        /// <summary>优先级（数值越小优先级越高）</summary>
        public int Priority { get; init; } = 10;

        /// <summary>任务完成回调</summary>
        public TaskCompletionSource<bool>? CompletionSource { get; init; }
    }

    /// <summary>
    /// 运动调度器 — 管理运动任务的排队和执行。
    /// </summary>
    public class MotionScheduler : IDisposable
    {
        private readonly AxisManager _axisManager;
        private readonly IEventBus _eventBus;
        private readonly BlockingCollection<MotionTask> _taskQueue = new();
        private CancellationTokenSource? _schedulerCts;
        private int _taskIdCounter;
        private bool _isRunning;

        /// <summary>是否正在运行</summary>
        public bool IsRunning => _isRunning;

        /// <summary>队列中等待的任务数</summary>
        public int PendingCount => _taskQueue.Count;

        /// <summary>当前正在执行的任务</summary>
        public MotionTask? CurrentTask { get; private set; }

        public MotionScheduler(AxisManager axisManager, IEventBus eventBus)
        {
            _axisManager = axisManager;
            _eventBus = eventBus;
        }

        /// <summary>
        /// 启动调度器 — 开始从队列消费运动任务。
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;

            _schedulerCts = new CancellationTokenSource();
            _isRunning = true;

            // 启动消费者线程
            _ = Task.Factory.StartNew(
                () => SchedulerLoop(_schedulerCts.Token),
                _schedulerCts.Token,
                TaskCreationOptions.LongRunning, // 使用专用线程
                TaskScheduler.Default);
        }

        /// <summary>
        /// 停止调度器。
        /// </summary>
        public void Stop()
        {
            _schedulerCts?.Cancel();
            _isRunning = false;
        }

        /// <summary>
        /// 提交运动任务到队列。
        /// </summary>
        /// <returns>可等待的Task，运动完成时完成</returns>
        public Task<bool> EnqueueAsync(MotionTask task)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var taskWithCallback = new MotionTask
            {
                TaskId = Interlocked.Increment(ref _taskIdCounter),
                Name = task.Name,
                Mode = task.Mode,
                AxisIds = task.AxisIds,
                Positions = task.Positions,
                Velocity = task.Velocity,
                Acceleration = task.Acceleration,
                Deceleration = task.Deceleration,
                Priority = task.Priority,
                CompletionSource = tcs
            };

            _taskQueue.Add(taskWithCallback);
            return tcs.Task;
        }

        /// <summary>
        /// 提交绝对定位运动任务（简化API）。
        /// </summary>
        public Task<bool> MoveAbsoluteAsync(int axisId, double position, double velocity)
        {
            return EnqueueAsync(new MotionTask
            {
                Mode = MotionMode.Absolute,
                AxisIds = new[] { axisId },
                Positions = new[] { position },
                Velocity = velocity,
                Acceleration = 500,
                Deceleration = 500
            });
        }

        /// <summary>
        /// 清空任务队列。
        /// </summary>
        public void ClearQueue()
        {
            while (_taskQueue.TryTake(out var task))
            {
                task.CompletionSource?.TrySetResult(false);
            }
        }

        /// <summary>
        /// 调度器主循环 — 生产者-消费者模式的消费者。
        /// </summary>
        private async Task SchedulerLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 从队列取出任务（阻塞等待）
                    if (!_taskQueue.TryTake(out var task, 100))
                        continue;

                    CurrentTask = task;

                    // 执行运动任务
                    var success = await ExecuteTaskAsync(task, cancellationToken);
                    task.CompletionSource?.TrySetResult(success);

                    CurrentTask = null;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    CurrentTask?.CompletionSource?.TrySetResult(false);
                    CurrentTask = null;
                }
            }
        }

        /// <summary>
        /// 执行单个运动任务。
        /// </summary>
        private async Task<bool> ExecuteTaskAsync(MotionTask task, CancellationToken cancellationToken)
        {
            switch (task.Mode)
            {
                case MotionMode.Absolute:
                {
                    if (task.AxisIds.Length == 1)
                    {
                        // 单轴绝对运动
                        var axis = _axisManager.GetAxis(task.AxisIds[0]);
                        if (axis == null) return false;
                        return await axis.MoveAbsoluteAsync(task.Positions[0],
                            task.Velocity, task.Acceleration, task.Deceleration, cancellationToken);
                    }
                    else
                    {
                        // 多轴直线插补
                        return await _axisManager.LinearMoveAsync(task.AxisIds, task.Positions,
                            task.Velocity, cancellationToken);
                    }
                }

                case MotionMode.Relative:
                {
                    var axis = _axisManager.GetAxis(task.AxisIds[0]);
                    if (axis == null) return false;
                    return await axis.MoveRelativeAsync(task.Positions[0],
                        task.Velocity, task.Acceleration, task.Deceleration, cancellationToken);
                }

                case MotionMode.Home:
                {
                    var axis = _axisManager.GetAxis(task.AxisIds[0]);
                    if (axis == null) return false;
                    return await axis.HomeAsync(cancellationToken);
                }

                case MotionMode.Jog:
                {
                    var axis = _axisManager.GetAxis(task.AxisIds[0]);
                    if (axis == null) return false;
                    return await axis.JogAsync(task.Velocity, task.Positions[0] > 0);
                }

                default:
                    return false;
            }
        }

        public void Dispose()
        {
            Stop();
            _taskQueue.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
