using SmartMES.Core.Scheduler;

namespace SmartMES.Tests;

public class TaskSchedulerServiceTests
{
    [Fact]
    /// <summary>
    /// 自动补齐：OnceTask_ShouldRunAndComplete 方法说明。
    /// </summary>
    public async Task OnceTask_ShouldRunAndComplete()
    {
        var scheduler = new TaskSchedulerService();
        var runCount = 0;

        scheduler.AddTask(ScheduledTaskFactory.Once("UT-Once", _ =>
        {
            Interlocked.Increment(ref runCount);
            return Task.CompletedTask;
        }));

        using var cts = new CancellationTokenSource();
        var loop = scheduler.StartAsync(cts.Token);

        await Task.Delay(180);
        cts.Cancel();
        await loop;

        Assert.True(runCount >= 1);
        var task = scheduler.GetAllTasks().First();
        Assert.Equal(ScheduledTaskState.Completed, task.State);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：PeriodicTask_ShouldRunMultipleTimes 方法说明。
    /// </summary>
    public async Task PeriodicTask_ShouldRunMultipleTimes()
    {
        var scheduler = new TaskSchedulerService();
        var runCount = 0;

        scheduler.AddTask(ScheduledTaskFactory.Periodic("UT-Periodic", TimeSpan.FromMilliseconds(80), _ =>
        {
            Interlocked.Increment(ref runCount);
            return Task.CompletedTask;
        }));

        using var cts = new CancellationTokenSource();
        var loop = scheduler.StartAsync(cts.Token);

        await Task.Delay(320);
        cts.Cancel();
        await loop;

        Assert.True(runCount >= 2);
    }

    [Fact]
    /// <summary>
    /// 自动补齐：FaultedTask_ShouldMarkFaultedAndStoreError 方法说明。
    /// </summary>
    public async Task FaultedTask_ShouldMarkFaultedAndStoreError()
    {
        var scheduler = new TaskSchedulerService();

        scheduler.AddTask(ScheduledTaskFactory.Once("UT-Fault", _ => throw new InvalidOperationException("boom")));

        using var cts = new CancellationTokenSource();
        var loop = scheduler.StartAsync(cts.Token);

        await Task.Delay(180);
        cts.Cancel();
        await loop;

        var t = scheduler.GetAllTasks().First();
        Assert.Equal(ScheduledTaskState.Faulted, t.State);
        Assert.Contains("boom", t.LastError);
    }
}
