// ============================================================
// 文件：IndustrialTaskScheduler.cs
// 用途：工业级多线程任务调度系统
// 设计思路：
//   工业设备中存在多种线程需求：
//   1. 实时线程 — 运动控制状态刷新（1ms~10ms周期）
//   2. 高优先级线程 — IO监控、安全互锁检查（10ms~50ms）
//   3. 普通线程 — 视觉处理、通讯处理（100ms级别）
//   4. 后台线程 — 日志写入、数据统计（秒级别）
//
//   如何避免常见问题：
//   - UI卡死 → 所有耗时操作在后台线程执行，通过Dispatcher更新UI
//   - 死锁 → 使用async/await替代同步等待，最小化锁范围
//   - 资源竞争 → 使用ConcurrentDictionary等线程安全集合
//   - 线程泄漏 → 使用CancellationToken统一管理线程生命周期
// ============================================================

using System.Collections.Concurrent;

namespace SmartSemiCon.Application.TaskScheduler
{
    /// <summary>
    /// 任务优先级。
    /// </summary>
    public enum TaskPriority
    {
        /// <summary>实时 — 最高优先级（运动控制、安全互锁）</summary>
        RealTime = 0,

        /// <summary>高 — IO监控、设备状态更新</summary>
        High = 1,

        /// <summary>普通 — 视觉处理、通讯处理</summary>
        Normal = 2,

        /// <summary>后台 — 日志、统计、数据保存</summary>
        Background = 3
    }

    /// <summary>
    /// 调度任务定义。
    /// </summary>
    public class ScheduledTask
    {
        /// <summary>任务名称</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>优先级</summary>
        public TaskPriority Priority { get; init; }

        /// <summary>执行间隔（毫秒）</summary>
        public int IntervalMs { get; init; }

        /// <summary>任务执行委托</summary>
        public Func<CancellationToken, Task> Action { get; init; } = _ => Task.CompletedTask;

        /// <summary>是否正在运行</summary>
        public bool IsRunning { get; set; }

        /// <summary>上次执行时间</summary>
        public DateTime? LastExecuteTime { get; set; }

        /// <summary>执行次数</summary>
        public long ExecutionCount { get; set; }

        /// <summary>错误次数</summary>
        public long ErrorCount { get; set; }

        /// <summary>平均执行耗时（毫秒）</summary>
        public double AverageExecutionTimeMs { get; set; }
    }

    /// <summary>
    /// 工业级任务调度器。
    /// 管理多个周期性任务，每个任务在独立线程中按指定间隔执行。
    /// </summary>
    public class IndustrialTaskScheduler : IDisposable
    {
        private readonly ConcurrentDictionary<string, ScheduledTask> _tasks = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _taskCts = new();
        private CancellationTokenSource? _globalCts;
        private bool _isRunning;

        /// <summary>是否正在运行</summary>
        public bool IsRunning => _isRunning;

        /// <summary>已注册的任务列表</summary>
        public IReadOnlyList<ScheduledTask> Tasks => _tasks.Values.ToList().AsReadOnly();

        /// <summary>
        /// 注册周期性任务。
        /// </summary>
        /// <param name="name">任务名称（唯一标识）</param>
        /// <param name="action">任务执行委托</param>
        /// <param name="intervalMs">执行间隔（毫秒）</param>
        /// <param name="priority">优先级</param>
        public void RegisterTask(string name, Func<CancellationToken, Task> action,
            int intervalMs, TaskPriority priority = TaskPriority.Normal)
        {
            _tasks[name] = new ScheduledTask
            {
                Name = name,
                Action = action,
                IntervalMs = intervalMs,
                Priority = priority
            };
        }

        /// <summary>
        /// 启动所有已注册的任务。
        /// </summary>
        public void StartAll()
        {
            if (_isRunning) return;

            _globalCts = new CancellationTokenSource();
            _isRunning = true;

            foreach (var task in _tasks.Values.OrderBy(t => t.Priority))
            {
                StartTask(task);
            }
        }

        /// <summary>
        /// 停止所有任务。
        /// </summary>
        public void StopAll()
        {
            _globalCts?.Cancel();

            foreach (var cts in _taskCts.Values)
            {
                cts.Cancel();
            }
            _taskCts.Clear();

            foreach (var task in _tasks.Values)
            {
                task.IsRunning = false;
            }

            _isRunning = false;
        }

        /// <summary>
        /// 启动单个任务。
        /// </summary>
        private void StartTask(ScheduledTask task)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts!.Token);
            _taskCts[task.Name] = cts;
            task.IsRunning = true;

            // 根据优先级选择线程创建方式
            var options = task.Priority == TaskPriority.RealTime
                ? TaskCreationOptions.LongRunning  // 实时任务使用专用线程
                : TaskCreationOptions.None;         // 普通任务使用线程池

            _ = Task.Factory.StartNew(async () =>
            {
                // 实时任务提升线程优先级
                if (task.Priority == TaskPriority.RealTime)
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Highest;
                }

                while (!cts.Token.IsCancellationRequested)
                {
                    var startTime = DateTime.Now;

                    try
                    {
                        await task.Action(cts.Token);
                        task.ExecutionCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        task.ErrorCount++;
                    }

                    task.LastExecuteTime = DateTime.Now;
                    var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    task.AverageExecutionTimeMs = (task.AverageExecutionTimeMs * 0.9) + (elapsed * 0.1);

                    // 等待到下一个周期
                    var waitTime = task.IntervalMs - (int)elapsed;
                    if (waitTime > 0)
                    {
                        try { await Task.Delay(waitTime, cts.Token); }
                        catch (OperationCanceledException) { break; }
                    }
                }

                task.IsRunning = false;
            }, cts.Token, options, System.Threading.Tasks.TaskScheduler.Default);
        }

        /// <summary>
        /// 提交一次性任务到后台执行。
        /// </summary>
        public Task<TResult> RunAsync<TResult>(Func<CancellationToken, Task<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => action(cancellationToken), cancellationToken);
        }

        public void Dispose()
        {
            StopAll();
            GC.SuppressFinalize(this);
        }
    }
}
