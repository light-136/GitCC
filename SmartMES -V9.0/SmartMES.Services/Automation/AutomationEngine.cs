using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Services.Automation
{
    /// <summary>步骤4：执行动作。</summary>
    public class ExecuteActionStep : AutomationStepBase
    {
        public override string StepName => "执行动作";
        public override string Description => "根据判断结果触发报警或记录正常状态";

        private readonly IAlarmService _alarmService;
        private readonly ILoggingService _logger;

        /// <summary>构造执行动作步骤。</summary>
        public ExecuteActionStep(IAlarmService alarmService, ILoggingService logger)
        {
            _alarmService = alarmService;
            _logger = logger;
        }

        /// <summary>执行动作步骤逻辑。</summary>
        public override async Task<StepResult> ExecuteAsync(AutomationContext context)
        {
            SetStatus(StepStatus.Running, "执行动作...");
            await Task.Delay(200);

            var passed = context.Get<bool>("ConditionPassed");
            var value = context.Get<double>("CollectedValue");

            if (!passed)
            {
                _alarmService.TriggerAlarm("ALM-001", $"检测值 {value:F2} 超出阈值范围", AlarmLevel.Warning);
                _logger.LogWarning($"自动化流程：检测值超限 {value:F2}", "Automation");
            }
            else
            {
                _logger.LogInfo($"自动化流程：检测正常，值={value:F2}", "Automation");
            }

            Progress = 100;
            SetStatus(StepStatus.Completed, "动作已执行");
            return new StepResult { Success = true, Message = "动作执行完毕" };
        }
    }

    /// <summary>自动化流程引擎。</summary>
    public class AutomationEngine
    {
        private readonly List<AutomationStepBase> _steps = new();
        private readonly ILoggingService _logger;
        private bool _isRunning = false;

        /// <summary>流程是否正在运行。</summary>
        public bool IsRunning => _isRunning;

        /// <summary>全部步骤列表。</summary>
        public IReadOnlyList<AutomationStepBase> Steps => _steps.AsReadOnly();

        /// <summary>当前执行步骤索引。</summary>
        public int CurrentStepIndex { get; private set; } = -1;

        /// <summary>流程完成事件。</summary>
        public event EventHandler<bool>? FlowCompleted;

        /// <summary>步骤切换事件。</summary>
        public event EventHandler<string>? StepChanged;

        /// <summary>构造自动化流程引擎。</summary>
        public AutomationEngine(ILoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>添加流程步骤。</summary>
        public void AddStep(AutomationStepBase step)
        {
            _steps.Add(step);
        }

        /// <summary>顺序执行全部步骤。</summary>
        public async Task RunAsync()
        {
            if (_isRunning)
            {
                _logger.LogWarning("流程已在运行中，忽略重复启动请求", "Automation");
                return;
            }

            _isRunning = true;
            var context = new AutomationContext();
            _logger.LogInfo("自动化流程启动", "Automation");

            try
            {
                for (int i = 0; i < _steps.Count; i++)
                {
                    CurrentStepIndex = i;
                    var step = _steps[i];
                    StepChanged?.Invoke(this, step.StepName);
                    _logger.LogInfo($"执行步骤 [{i + 1}/{_steps.Count}]: {step.StepName}", "Automation");

                    var result = await step.ExecuteAsync(context);
                    if (!result.Success)
                    {
                        _logger.LogError($"步骤 [{step.StepName}] 执行失败: {result.Message}", "Automation");
                        FlowCompleted?.Invoke(this, false);
                        return;
                    }

                    if (!result.ContinueNext)
                    {
                        _logger.LogInfo($"步骤 [{step.StepName}] 指示停止流程", "Automation");
                        break;
                    }
                }

                _logger.LogInfo("自动化流程执行完成", "Automation");
                FlowCompleted?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                _logger.LogError($"流程异常终止: {ex.Message}", "Automation");
                FlowCompleted?.Invoke(this, false);
            }
            finally
            {
                _isRunning = false;
                CurrentStepIndex = -1;
            }
        }

        /// <summary>重置步骤状态。</summary>
        public void Reset()
        {
            CurrentStepIndex = -1;
            foreach (var step in _steps)
                step.Status = StepStatus.Pending;
        }
    }
}
