namespace SmartMES.Core.Scheduler
{
    /// <summary>浠诲姟浼樺厛绾э紙鏁板瓧瓒婂皬浼樺厛绾ц秺楂橈級</summary>
    public enum TaskPriority { Critical = 0, High = 1, Normal = 2, Low = 3 }

    /// <summary>璋冨害浠诲姟鐘舵€?/summary>
    public enum ScheduledTaskState { Pending, Running, Completed, Faulted, Cancelled }

    /// <summary>
    /// 璋冨害浠诲姟妯″瀷
    /// 鏀寔锛氬懆鏈熶换鍔°€佸欢杩熶换鍔°€佸崟娆′换鍔°€佷紭鍏堢骇
    /// </summary>
    public class ScheduledTask
    {
        public Guid   Id       { get; } = Guid.NewGuid();
        public string Name     { get; init; } = string.Empty;
        public TaskPriority Priority { get; init; } = TaskPriority.Normal;
        /// <summary>鍛ㄦ湡浠诲姟闂撮殧锛宯ull=鍗曟浠诲姟</summary>
        public TimeSpan? Interval  { get; init; }
        /// <summary>棣栨鎵ц寤惰繜</summary>
        public TimeSpan  Delay     { get; init; } = TimeSpan.Zero;
        public Func<CancellationToken, Task> Action { get; init; } = _ => Task.CompletedTask;
        public ScheduledTaskState State { get; internal set; } = ScheduledTaskState.Pending;
        public DateTime?  LastRunTime   { get; internal set; }
        public DateTime?  NextRunTime   { get; internal set; }
        public string     LastError     { get; internal set; } = string.Empty;
        public int        RunCount      { get; internal set; }
        public bool       IsEnabled     { get; set; } = true;
    }

    /// <summary>璋冨害鍣ㄦ湇鍔℃帴鍙?/summary>
    public interface ISchedulerService
    {
        Guid   AddTask(ScheduledTask task);
        void   RemoveTask(Guid taskId);
        void   EnableTask(Guid taskId);
        void   DisableTask(Guid taskId);
        Task   StartAsync(CancellationToken ct = default);
        void   Stop();
        IReadOnlyList<ScheduledTask> GetAllTasks();
        event EventHandler<ScheduledTask>?                       TaskCompleted;
        event EventHandler<(ScheduledTask Task, Exception Ex)>? TaskFaulted;
    }

    /// <summary>
    /// 缁熶竴浠诲姟璋冨害涓績 鈥?绯荤粺"蹇冭剰"
    /// 鎵€鏈夎澶囪疆璇€侀€氫俊銆佽嚜鍔ㄥ寲琛屼负鍧囬€氳繃姝よ皟搴﹀櫒鎵ц
    /// 璋冨害绮惧害锛?0ms
    /// </summary>
    public class TaskSchedulerService : ISchedulerService
    {
        private readonly List<ScheduledTask> _tasks = new();
        private readonly object _lock = new();
        private CancellationTokenSource _cts = new();

        public event EventHandler<ScheduledTask>?                       TaskCompleted;
        public event EventHandler<(ScheduledTask Task, Exception Ex)>? TaskFaulted;

        /// <summary>
        /// 自动补齐：AddTask 方法说明。
        /// </summary>
        public Guid AddTask(ScheduledTask task)
        {
            lock (_lock)
            {
                task.NextRunTime = DateTime.Now + task.Delay;
                _tasks.Add(task);
            }
            return task.Id;
        }

        /// <summary>
        /// 自动补齐：RemoveTask 方法说明。
        /// </summary>
        public void RemoveTask(Guid id)
        { lock (_lock) _tasks.RemoveAll(t => t.Id == id); }

        /// <summary>
        /// 自动补齐：EnableTask 方法说明。
        /// </summary>
        public void EnableTask(Guid id)
        { lock (_lock) { var t = _tasks.FirstOrDefault(x => x.Id == id); if (t != null) t.IsEnabled = true; } }

        /// <summary>
        /// 自动补齐：DisableTask 方法说明。
        /// </summary>
        public void DisableTask(Guid id)
        { lock (_lock) { var t = _tasks.FirstOrDefault(x => x.Id == id); if (t != null) t.IsEnabled = false; } }

        /// <summary>
        /// 自动补齐：GetAllTasks 方法说明。
        /// </summary>
        public IReadOnlyList<ScheduledTask> GetAllTasks()
        { lock (_lock) return _tasks.OrderBy(t => (int)t.Priority).ToList(); }

        /// <summary>
        /// 自动补齐：StartAsync 方法说明。
        /// </summary>
        public async Task StartAsync(CancellationToken ct = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            while (!_cts.Token.IsCancellationRequested)
            {
                List<ScheduledTask> due;
                lock (_lock)
                    due = _tasks
                        .Where(t => t.IsEnabled
                            && t.State != ScheduledTaskState.Running
                            && t.NextRunTime <= DateTime.Now)
                        .OrderBy(t => (int)t.Priority)
                        .ToList();

                if (due.Count > 0)
                    await Task.WhenAll(due.Select(t => ExecuteTaskAsync(t, _cts.Token)));

                await Task.Delay(50, _cts.Token).ContinueWith(_ => { });
            }
        }

        /// <summary>
        /// 自动补齐：Stop 方法说明。
        /// </summary>
        public void Stop() => _cts.Cancel();

        /// <summary>
        /// 自动补齐：ExecuteTaskAsync 方法说明。
        /// </summary>
        private async Task ExecuteTaskAsync(ScheduledTask task, CancellationToken ct)
        {
            task.State = ScheduledTaskState.Running;
            task.LastRunTime = DateTime.Now;
            task.RunCount++;
            try
            {
                await task.Action(ct);
                task.State = task.Interval.HasValue ? ScheduledTaskState.Pending : ScheduledTaskState.Completed;
                task.NextRunTime = task.Interval.HasValue ? DateTime.Now + task.Interval.Value : null;
                TaskCompleted?.Invoke(this, task);
            }
            catch (OperationCanceledException) { task.State = ScheduledTaskState.Cancelled; }
            catch (Exception ex)
            {
                task.State = ScheduledTaskState.Faulted;
                task.LastError = ex.Message;
                TaskFaulted?.Invoke(this, (task, ex));
                if (task.Interval.HasValue) task.NextRunTime = DateTime.Now + TimeSpan.FromSeconds(1);
            }
        }
    }

    /// <summary>璋冨害浠诲姟渚挎嵎宸ュ巶</summary>
    public static class ScheduledTaskFactory
    {
        /// <summary>
        /// 自动补齐：Periodic 方法说明。
        /// </summary>
        public static ScheduledTask Periodic(string name, TimeSpan interval,
            Func<CancellationToken, Task> action, TaskPriority priority = TaskPriority.Normal,
            TimeSpan delay = default) =>
            new() { Name=name, Interval=interval, Action=action, Priority=priority, Delay=delay };

        /// <summary>
        /// 自动补齐：Once 方法说明。
        /// </summary>
        public static ScheduledTask Once(string name, Func<CancellationToken, Task> action,
            TaskPriority priority = TaskPriority.Normal, TimeSpan delay = default) =>
            new() { Name=name, Action=action, Priority=priority, Delay=delay };

        /// <summary>
        /// 自动补齐：EStop 方法说明。
        /// </summary>
        public static ScheduledTask EStop(Func<CancellationToken, Task> action) =>
            new() { Name="[ESTOP]", Priority=TaskPriority.Critical, Action=action };
    }
}
