// ============================================================
// 文件：AutomationService.cs
// 层次：应用层 (Application Layer) — 自动化流程服务
// 职责：
//   编排运动控制、视觉检测、IO 控制等硬件模块，实现自动化生产流程。
//   提供启动/暂停/恢复/停止自动运行的全局控制接口。
// 设计思路：
//   自动化流程定义为有序步骤列表（Step），每步包含动作、条件判断、超时。
//   步骤执行引擎按顺序执行，支持条件跳转、循环、报警中断。
// ============================================================

using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.Interfaces;

namespace SmartIndustry.Application.Automation
{
    /// <summary>自动运行状态</summary>
    public enum AutoRunState { Idle, Running, Paused, Stopping, Completed, Faulted }

    /// <summary>
    /// 流程步骤定义
    /// </summary>
    public class ProcessStep
    {
        public int StepIndex { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public Func<CancellationToken, Task<bool>> Action { get; init; } = _ => Task.FromResult(true);
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
        public bool StopOnFailure { get; init; } = true;
    }

    /// <summary>
    /// 流程执行结果
    /// </summary>
    public class ProcessResult
    {
        public bool IsSuccess { get; init; }
        public int CompletedSteps { get; init; }
        public int TotalSteps { get; init; }
        public TimeSpan TotalDuration { get; init; }
        public string? ErrorMessage { get; init; }
        public int? FailedStepIndex { get; init; }
    }

    /// <summary>
    /// 自动化流程服务
    /// </summary>
    public class AutomationService
    {
        private readonly IEventBus _eventBus;
        private readonly ILogService _logService;

        private AutoRunState _state = AutoRunState.Idle;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _pauseGate = new(1, 1);
        private bool _isPaused;

        private List<ProcessStep> _steps = new();
        private int _currentStepIndex;
        private int _cycleCount;

        /// <summary>当前自动运行状态</summary>
        public AutoRunState State => _state;

        /// <summary>当前执行步骤索引</summary>
        public int CurrentStepIndex => _currentStepIndex;

        /// <summary>已完成循环次数</summary>
        public int CycleCount => _cycleCount;

        /// <summary>状态变更事件</summary>
        public event Action<AutoRunState>? StateChanged;

        /// <summary>步骤完成事件</summary>
        public event Action<int, string, bool>? StepCompleted;

        public AutomationService(IEventBus eventBus, ILogService logService)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        /// <summary>加载流程定义</summary>
        public void LoadProcess(IEnumerable<ProcessStep> steps)
        {
            _steps = steps.OrderBy(s => s.StepIndex).ToList();
            _logService.Info("Automation", $"已加载流程定义：{_steps.Count} 个步骤");
        }

        /// <summary>启动自动运行</summary>
        public async Task<ProcessResult> StartAsync(int maxCycles = 1)
        {
            if (_state == AutoRunState.Running)
                throw new InvalidOperationException("自动运行已在执行中");

            if (_steps.Count == 0)
                throw new InvalidOperationException("未加载流程定义");

            _cts = new CancellationTokenSource();
            _state = AutoRunState.Running;
            _cycleCount = 0;
            _isPaused = false;
            StateChanged?.Invoke(_state);

            _logService.Info("Automation", $"自动运行启动，目标循环：{maxCycles}");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var totalCompleted = 0;

            try
            {
                for (int cycle = 0; cycle < maxCycles && !_cts.Token.IsCancellationRequested; cycle++)
                {
                    for (_currentStepIndex = 0; _currentStepIndex < _steps.Count; _currentStepIndex++)
                    {
                        // 暂停检查
                        if (_isPaused)
                        {
                            await _pauseGate.WaitAsync(_cts.Token);
                            _pauseGate.Release();
                        }

                        _cts.Token.ThrowIfCancellationRequested();

                        var step = _steps[_currentStepIndex];
                        var success = false;

                        try
                        {
                            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                            timeoutCts.CancelAfter(step.Timeout);

                            success = await step.Action(timeoutCts.Token);
                            totalCompleted++;
                        }
                        catch (OperationCanceledException) when (!_cts.Token.IsCancellationRequested)
                        {
                            _logService.Error("Automation", $"步骤超时：[{step.StepIndex}] {step.Name}");
                            success = false;
                        }

                        StepCompleted?.Invoke(step.StepIndex, step.Name, success);

                        if (!success && step.StopOnFailure)
                        {
                            sw.Stop();
                            _state = AutoRunState.Faulted;
                            StateChanged?.Invoke(_state);
                            return new ProcessResult
                            {
                                IsSuccess = false,
                                CompletedSteps = totalCompleted,
                                TotalSteps = _steps.Count * maxCycles,
                                TotalDuration = sw.Elapsed,
                                ErrorMessage = $"步骤 [{step.StepIndex}] {step.Name} 执行失败",
                                FailedStepIndex = step.StepIndex
                            };
                        }
                    }
                    _cycleCount++;
                }
            }
            catch (OperationCanceledException)
            {
                _logService.Info("Automation", "自动运行已停止");
            }

            sw.Stop();
            _state = AutoRunState.Completed;
            StateChanged?.Invoke(_state);

            _logService.Info("Automation",
                $"自动运行完成：{_cycleCount} 循环，{totalCompleted} 步，耗时 {sw.Elapsed.TotalSeconds:F1}s");

            return new ProcessResult
            {
                IsSuccess = true,
                CompletedSteps = totalCompleted,
                TotalSteps = _steps.Count * maxCycles,
                TotalDuration = sw.Elapsed
            };
        }

        /// <summary>暂停自动运行</summary>
        public void Pause()
        {
            if (_state != AutoRunState.Running) return;
            _isPaused = true;
            _pauseGate.Wait();
            _state = AutoRunState.Paused;
            StateChanged?.Invoke(_state);
            _logService.Info("Automation", "自动运行已暂停");
        }

        /// <summary>恢复自动运行</summary>
        public void Resume()
        {
            if (_state != AutoRunState.Paused) return;
            _isPaused = false;
            _pauseGate.Release();
            _state = AutoRunState.Running;
            StateChanged?.Invoke(_state);
            _logService.Info("Automation", "自动运行已恢复");
        }

        /// <summary>停止自动运行</summary>
        public void Stop()
        {
            if (_state == AutoRunState.Idle || _state == AutoRunState.Completed) return;

            _cts?.Cancel();
            if (_isPaused)
            {
                _isPaused = false;
                try { _pauseGate.Release(); } catch { }
            }

            _state = AutoRunState.Stopping;
            StateChanged?.Invoke(_state);
            _logService.Info("Automation", "自动运行停止中...");
        }
    }
}
