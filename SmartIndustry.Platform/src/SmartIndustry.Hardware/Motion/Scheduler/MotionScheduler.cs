// ============================================================
// 文件：MotionScheduler.cs
// 层级：硬件抽象层（Hardware Layer）> Motion > Scheduler
// 职责：运动任务调度器。
//       使用生产者-消费者模式，将外部提交的运动任务按优先级排队，
//       由后台线程顺序执行，避免多线程并发运动导致的轴冲突。
//
// 核心设计：
//   1. 4个优先级队列，各自用 BlockingCollection<MotionTask> 实现
//   2. RealTime 任务使用 LongRunning 专用线程（不共用线程池），
//      其余任务共用一个消费者线程
//   3. TakeFromAny 从4个队列中按优先级顺序取任务（RealTime先）
//   4. 任务执行统计：记录每个任务的执行时间，维护滚动窗口统计
//   5. 暂停/恢复：ManualResetEventSlim 实现，暂停时任务仍可入队
//   6. 统一 CancellationToken 生命周期控制（Shutdown时取消）
//
// 线程模型：
//   - 提交线程（任意外部线程）：调用 Enqueue 写入队列
//   - RealTime 消费线程（LongRunning）：仅处理 RealTime 优先级任务
//   - Normal 消费线程：处理 High/Normal/Background 优先级任务
//   - 两个消费线程各自独立，互不干扰
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using SmartIndustry.Domain.Enums;
using SmartIndustry.Hardware.Motion.Drivers;

namespace SmartIndustry.Hardware.Motion.Scheduler
{
    /// <summary>
    /// 任务调度统计信息 — 描述一段时间内调度器的运行指标。
    /// </summary>
    public class SchedulerStatistics
    {
        /// <summary>已完成任务总数</summary>
        public long TotalCompleted { get; set; }

        /// <summary>失败任务总数</summary>
        public long TotalFailed { get; set; }

        /// <summary>取消任务总数</summary>
        public long TotalCancelled { get; set; }

        /// <summary>队列当前深度（各优先级队列总和）</summary>
        public int QueueDepth { get; set; }

        /// <summary>平均执行时间（ms）</summary>
        public double AverageExecutionMs { get; set; }

        /// <summary>最大执行时间（ms）</summary>
        public double MaxExecutionMs { get; set; }

        /// <summary>是否处于暂停状态</summary>
        public bool IsPaused { get; set; }

        /// <summary>统计快照时间戳</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 运动任务调度器。
    /// 负责接收来自应用层的运动任务，按优先级排队，驱动 AxisController 执行。
    ///
    /// 使用方式：
    ///   var scheduler = new MotionScheduler(axisMap);
    ///   scheduler.Start();
    ///   scheduler.Enqueue(new MotionTask { AxisId="X", TargetPosition=100 });
    ///   scheduler.Pause(); // 暂停（已运动中的任务不受影响）
    ///   scheduler.Resume();
    ///   scheduler.Shutdown(); // 停止调度器
    /// </summary>
    public sealed class MotionScheduler : IDisposable
    {
        // ==================== 私有字段 ====================

        /// <summary>轴控制器映射（Key=AxisId）</summary>
        private readonly IReadOnlyDictionary<string, AxisController> _axisMap;

        // 4个优先级队列（独立 BlockingCollection，容量限制防止内存溢出）
        private readonly BlockingCollection<MotionTask> _queueRealTime =
            new(new ConcurrentQueue<MotionTask>(), 100);
        private readonly BlockingCollection<MotionTask> _queueHigh =
            new(new ConcurrentQueue<MotionTask>(), 500);
        private readonly BlockingCollection<MotionTask> _queueNormal =
            new(new ConcurrentQueue<MotionTask>(), 1000);
        private readonly BlockingCollection<MotionTask> _queueBackground =
            new(new ConcurrentQueue<MotionTask>(), 500);

        /// <summary>统一生命周期取消源（Shutdown时取消）</summary>
        private readonly CancellationTokenSource _lifetimeCts = new();

        /// <summary>暂停控制事件（Set=运行，Reset=暂停）</summary>
        private readonly ManualResetEventSlim _pauseEvent = new(true);

        /// <summary>RealTime 消费线程</summary>
        private Thread? _realTimeThread;

        /// <summary>Normal 消费线程（处理 High/Normal/Background）</summary>
        private Thread? _normalThread;

        // 统计相关
        private long _totalCompleted;
        private long _totalFailed;
        private long _totalCancelled;
        private double _totalExecutionMs;
        private double _maxExecutionMs;
        private long _executionCount;
        private readonly object _statsLock = new();

        /// <summary>是否已启动</summary>
        private volatile bool _isStarted;

        /// <summary>是否已暂停</summary>
        private volatile bool _isPaused;

        // ==================== 事件 ====================

        /// <summary>任务开始执行时触发</summary>
        public event EventHandler<MotionTask>? TaskStarted;

        /// <summary>任务完成（成功/失败/取消）时触发</summary>
        public event EventHandler<MotionTaskResult>? TaskCompleted;

        /// <summary>队列深度超过警戒阈值时触发</summary>
        public event EventHandler<int>? QueueDepthWarning;

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造运动调度器
        /// </summary>
        /// <param name="axisMap">轴控制器映射（Key=AxisId）</param>
        public MotionScheduler(IReadOnlyDictionary<string, AxisController> axisMap)
        {
            _axisMap = axisMap ?? throw new ArgumentNullException(nameof(axisMap));
        }

        // ==================== 公开方法 ====================

        /// <summary>
        /// 启动调度器（创建消费者线程）。
        /// 必须在提交任务前调用。
        /// </summary>
        public void Start()
        {
            if (_isStarted) return;
            _isStarted = true;

            // RealTime 专用线程（LongRunning，固定在线程池外运行）
            _realTimeThread = new Thread(RealTimeConsumerLoop)
            {
                Name = "MotionScheduler-RealTime",
                IsBackground = true,
                Priority = ThreadPriority.Highest  // 实时线程使用最高优先级
            };
            _realTimeThread.Start();

            // Normal 线程（处理 High/Normal/Background）
            _normalThread = new Thread(NormalConsumerLoop)
            {
                Name = "MotionScheduler-Normal",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _normalThread.Start();
        }

        /// <summary>
        /// 提交运动任务到对应优先级队列。
        /// 线程安全，可从任意线程调用。
        /// </summary>
        /// <param name="task">运动任务</param>
        /// <exception cref="InvalidOperationException">调度器未启动或已关闭</exception>
        /// <exception cref="InvalidOperationException">对应优先级队列已满</exception>
        public void Enqueue(MotionTask task)
        {
            if (!_isStarted) throw new InvalidOperationException("调度器未启动，请先调用 Start()");
            if (_lifetimeCts.IsCancellationRequested) throw new InvalidOperationException("调度器已关闭");

            task.Status = MotionTaskStatus.Pending;

            var queue = GetQueue(task.Priority);
            if (!queue.TryAdd(task, 0)) // 0ms超时：队列满时立即失败
                throw new InvalidOperationException($"优先级[{task.Priority}]队列已满，任务提交失败");

            // 检查队列深度告警
            int depth = TotalQueueDepth;
            if (depth > 100)
                QueueDepthWarning?.Invoke(this, depth);
        }

        /// <summary>
        /// 暂停任务调度（等当前执行的任务完成后停止，新出队的任务不执行）。
        /// 已在队列中的任务保持，恢复后继续执行。
        /// </summary>
        public void Pause()
        {
            _isPaused = true;
            _pauseEvent.Reset(); // 关闭事件，让消费者线程阻塞在等待处
        }

        /// <summary>
        /// 恢复任务调度
        /// </summary>
        public void Resume()
        {
            _isPaused = false;
            _pauseEvent.Set(); // 开放事件，消费者线程继续执行
        }

        /// <summary>
        /// 停止调度器（取消所有等待中的任务，等待消费者线程退出）。
        /// 注意：已运动中的任务不会被强制停止，需外部调用 AxisController.Stop()。
        /// </summary>
        public void Shutdown()
        {
            if (!_isStarted) return;
            _isStarted = false;

            // 标记各队列完成（让 Take 的等待阻塞解除）
            _queueRealTime.CompleteAdding();
            _queueHigh.CompleteAdding();
            _queueNormal.CompleteAdding();
            _queueBackground.CompleteAdding();

            // 解除暂停（防止线程卡在暂停等待）
            _pauseEvent.Set();

            // 触发 CancellationToken（让 TakeFromAny 解除阻塞）
            _lifetimeCts.Cancel();

            // 等待消费者线程退出（最多5秒）
            _realTimeThread?.Join(5000);
            _normalThread?.Join(5000);
        }

        /// <summary>
        /// 获取当前调度统计信息快照
        /// </summary>
        public SchedulerStatistics GetStatistics()
        {
            lock (_statsLock)
            {
                return new SchedulerStatistics
                {
                    TotalCompleted = _totalCompleted,
                    TotalFailed = _totalFailed,
                    TotalCancelled = _totalCancelled,
                    QueueDepth = TotalQueueDepth,
                    AverageExecutionMs = _executionCount > 0 ? _totalExecutionMs / _executionCount : 0,
                    MaxExecutionMs = _maxExecutionMs,
                    IsPaused = _isPaused
                };
            }
        }

        /// <summary>当前队列总深度</summary>
        public int TotalQueueDepth
            => _queueRealTime.Count + _queueHigh.Count + _queueNormal.Count + _queueBackground.Count;

        // ==================== 消费者线程循环 ====================

        /// <summary>
        /// RealTime 优先级任务消费者循环（专用高优先级线程）
        /// </summary>
        private void RealTimeConsumerLoop()
        {
            foreach (var task in _queueRealTime.GetConsumingEnumerable(_lifetimeCts.Token))
            {
                if (_lifetimeCts.IsCancellationRequested) break;
                ExecuteTask(task);
            }
        }

        /// <summary>
        /// Normal/High/Background 优先级任务消费者循环。
        /// 使用 BlockingCollection.TakeFromAny 在三个队列上等待，
        /// 数组顺序（High、Normal、Background）决定优先级。
        /// </summary>
        private void NormalConsumerLoop()
        {
            // 按优先级顺序排列（TakeFromAny 总选最低索引有数据的队列）
            var queues = new[]
            {
                _queueHigh,
                _queueNormal,
                _queueBackground
            };

            while (!_lifetimeCts.IsCancellationRequested)
            {
                try
                {
                    // 等待暂停事件（暂停时阻塞在这里）
                    _pauseEvent.Wait(_lifetimeCts.Token);

                    // 从队列中取任务（最多等 500ms，避免永久阻塞）
                    int index = BlockingCollection<MotionTask>.TakeFromAny(
                        queues, out MotionTask? task, _lifetimeCts.Token);

                    if (task != null)
                        ExecuteTask(task);
                }
                catch (OperationCanceledException) { break; }
                catch (InvalidOperationException) { break; } // 队列已完成添加
            }
        }

        // ==================== 任务执行 ====================

        /// <summary>
        /// 执行单个运动任务。
        /// 流程：
        ///   1. 状态更新为 Running
        ///   2. 触发 TaskStarted 事件
        ///   3. 根据 MotionMode 调用对应的 AxisController 方法
        ///   4. 等待运动完成（AxisController 的 Async 方法 await）
        ///   5. 更新统计，触发 TaskCompleted 事件
        ///   6. 调用任务的 OnCompleted 回调
        /// </summary>
        private void ExecuteTask(MotionTask task)
        {
            // 检查暂停（RealTime 任务跳过暂停等待）
            if (task.Priority != MotionTaskPriority.RealTime)
                _pauseEvent.Wait(_lifetimeCts.Token);

            task.Status = MotionTaskStatus.Running;
            task.StartedAt = DateTime.Now;
            TaskStarted?.Invoke(this, task);

            var sw = Stopwatch.StartNew();
            bool success = false;
            string errorMessage = string.Empty;
            double actualPosition = 0;

            try
            {
                if (!_axisMap.TryGetValue(task.AxisId, out var axisController))
                {
                    errorMessage = $"轴[{task.AxisId}]不存在于轴映射表";
                    task.Status = MotionTaskStatus.Error;
                }
                else
                {
                    // 同步执行异步方法（调度器线程可以安全使用 .GetAwaiter().GetResult()）
                    success = task.MotionMode switch
                    {
                        MotionMode.Absolute => axisController.MoveAbsoluteAsync(
                            task.TargetPosition, task.Profile).GetAwaiter().GetResult(),
                        MotionMode.Relative => axisController.MoveRelativeAsync(
                            task.TargetPosition, task.Profile.MaxVelocity, task.Profile.Acceleration).GetAwaiter().GetResult(),
                        MotionMode.Home => axisController.HomeAsync(task.HomingConfig).GetAwaiter().GetResult(),
                        _ => false
                    };

                    actualPosition = axisController.GetActualPosition();
                    task.Status = success ? MotionTaskStatus.Completed : MotionTaskStatus.Error;
                    if (!success) errorMessage = "运动未能成功完成（超时或限位）";
                }
            }
            catch (OperationCanceledException)
            {
                task.Status = MotionTaskStatus.Cancelled;
                errorMessage = "任务被取消";
                Interlocked.Increment(ref _totalCancelled);
            }
            catch (Exception ex)
            {
                task.Status = MotionTaskStatus.Error;
                errorMessage = ex.Message;
            }
            finally
            {
                sw.Stop();
                task.FinishedAt = DateTime.Now;

                // 更新统计
                UpdateStatistics(success, sw.Elapsed.TotalMilliseconds);

                var result = new MotionTaskResult
                {
                    Task = task,
                    IsSuccess = success,
                    ActualPosition = actualPosition,
                    PositionError = Math.Abs(actualPosition - task.TargetPosition),
                    Duration = sw.Elapsed,
                    ErrorMessage = errorMessage
                };

                // 触发完成事件和回调
                TaskCompleted?.Invoke(this, result);
                task.OnCompleted?.Invoke(result);
            }
        }

        /// <summary>
        /// 更新执行统计数据（线程安全）
        /// </summary>
        private void UpdateStatistics(bool success, double executionMs)
        {
            lock (_statsLock)
            {
                if (success)
                    _totalCompleted++;
                else
                    _totalFailed++;

                _totalExecutionMs += executionMs;
                _executionCount++;
                if (executionMs > _maxExecutionMs)
                    _maxExecutionMs = executionMs;
            }
        }

        /// <summary>
        /// 根据优先级获取对应的队列
        /// </summary>
        private BlockingCollection<MotionTask> GetQueue(MotionTaskPriority priority)
            => priority switch
            {
                MotionTaskPriority.RealTime => _queueRealTime,
                MotionTaskPriority.High => _queueHigh,
                MotionTaskPriority.Normal => _queueNormal,
                MotionTaskPriority.Background => _queueBackground,
                _ => _queueNormal
            };

        // ==================== IDisposable ====================

        /// <inheritdoc/>
        public void Dispose()
        {
            Shutdown();
            _lifetimeCts.Dispose();
            _pauseEvent.Dispose();
            _queueRealTime.Dispose();
            _queueHigh.Dispose();
            _queueNormal.Dispose();
            _queueBackground.Dispose();
        }
    }
}
