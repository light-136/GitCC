// ============================================================
// 文件：AppTaskScheduler.cs
// 层次：应用层 (Application Layer) — 任务调度器
// 职责：
//   提供应用层的定时任务和周期任务调度功能。
//   支持一次性延迟任务和周期性重复任务。
// 设计思路：
//   基于 System.Threading.Timer 的轻量级调度器，
//   适用于工业设备的周期性巡检、状态轮询、Token 过期清理等场景。
//   每个任务有独立的 Timer，支持启动/暂停/取消。
// ============================================================

using SmartIndustry.Domain.Interfaces;

namespace SmartIndustry.Application.TaskScheduler
{
    /// <summary>
    /// 调度任务定义
    /// </summary>
    public class ScheduledTask
    {
        public string TaskId { get; init; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; init; } = string.Empty;
        public TimeSpan Interval { get; init; }
        public TimeSpan InitialDelay { get; init; } = TimeSpan.Zero;
        public bool IsRecurring { get; init; } = true;
        public Func<CancellationToken, Task> Action { get; init; } = _ => Task.CompletedTask;
        public bool IsRunning { get; set; }
        public DateTime? LastExecutedAt { get; set; }
        public int ExecutionCount { get; set; }
        public int ErrorCount { get; set; }
    }

    /// <summary>
    /// 应用层任务调度器
    /// </summary>
    public class AppTaskScheduler : IDisposable
    {
        private readonly ILogService _logService;
        private readonly Dictionary<string, (ScheduledTask Task, Timer Timer)> _tasks = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly object _lock = new();

        public AppTaskScheduler(ILogService logService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        /// <summary>注册并启动一个周期性任务</summary>
        public string Schedule(string name, TimeSpan interval, Func<CancellationToken, Task> action,
            TimeSpan? initialDelay = null)
        {
            var task = new ScheduledTask
            {
                Name = name,
                Interval = interval,
                InitialDelay = initialDelay ?? TimeSpan.Zero,
                IsRecurring = true,
                Action = action
            };

            return RegisterTask(task);
        }

        /// <summary>注册并启动一个一次性延迟任务</summary>
        public string ScheduleOnce(string name, TimeSpan delay, Func<CancellationToken, Task> action)
        {
            var task = new ScheduledTask
            {
                Name = name,
                Interval = delay,
                IsRecurring = false,
                Action = action
            };

            return RegisterTask(task);
        }

        /// <summary>取消指定任务</summary>
        public bool Cancel(string taskId)
        {
            lock (_lock)
            {
                if (_tasks.TryGetValue(taskId, out var entry))
                {
                    entry.Timer.Dispose();
                    entry.Task.IsRunning = false;
                    _tasks.Remove(taskId);
                    _logService.Info("TaskScheduler", $"任务已取消：{entry.Task.Name}（{taskId}）");
                    return true;
                }
            }
            return false;
        }

        /// <summary>获取所有已注册的任务</summary>
        public IReadOnlyList<ScheduledTask> GetAllTasks()
        {
            lock (_lock)
            {
                return _tasks.Values.Select(e => e.Task).ToList().AsReadOnly();
            }
        }

        private string RegisterTask(ScheduledTask task)
        {
            var timer = new Timer(async _ => await ExecuteTaskAsync(task),
                null,
                task.IsRecurring ? task.InitialDelay : task.Interval,
                task.IsRecurring ? task.Interval : Timeout.InfiniteTimeSpan);

            lock (_lock)
            {
                task.IsRunning = true;
                _tasks[task.TaskId] = (task, timer);
            }

            _logService.Info("TaskScheduler",
                $"任务已注册：{task.Name}（{task.TaskId}），间隔：{task.Interval.TotalSeconds}s");

            return task.TaskId;
        }

        private async Task ExecuteTaskAsync(ScheduledTask task)
        {
            try
            {
                await task.Action(_cts.Token);
                task.LastExecutedAt = DateTime.Now;
                task.ExecutionCount++;
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略
            }
            catch (Exception ex)
            {
                task.ErrorCount++;
                _logService.Error("TaskScheduler", $"任务执行异常：{task.Name} - {ex.Message}", ex);
            }

            // 一次性任务执行完毕后自动清理
            if (!task.IsRecurring)
            {
                Cancel(task.TaskId);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            lock (_lock)
            {
                foreach (var (_, (_, timer)) in _tasks)
                    timer.Dispose();
                _tasks.Clear();
            }
            _cts.Dispose();
        }
    }
}
