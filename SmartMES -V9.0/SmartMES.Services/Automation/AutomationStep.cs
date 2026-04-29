using SmartMES.Core.Interfaces;

namespace SmartMES.Services.Automation
{
    /// <summary>流程步骤执行结果。</summary>
    public class StepResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        /// <summary>是否继续执行下一步。</summary>
        public bool ContinueNext { get; set; } = true;
    }

    /// <summary>自动化步骤抽象基类。</summary>
    public abstract class AutomationStepBase
    {
        public abstract string StepName { get; }
        public abstract string Description { get; }

        public StepStatus Status { get; set; } = StepStatus.Pending;
        public int Progress { get; protected set; } = 0;

        public event EventHandler<StepStatusChangedArgs>? StatusChanged;

        /// <summary>执行当前步骤。</summary>
        public abstract Task<StepResult> ExecuteAsync(AutomationContext context);

        /// <summary>更新步骤状态并广播事件。</summary>
        protected void SetStatus(StepStatus status, string message = "")
        {
            Status = status;
            StatusChanged?.Invoke(this, new StepStatusChangedArgs(StepName, status, message));
        }
    }

    /// <summary>步骤状态。</summary>
    public enum StepStatus { Pending, Running, Completed, Failed, Skipped }

    /// <summary>步骤状态变更事件参数。</summary>
    public class StepStatusChangedArgs : EventArgs
    {
        public string StepName { get; }
        public StepStatus Status { get; }
        public string Message { get; }

        /// <summary>构造状态变更事件参数。</summary>
        public StepStatusChangedArgs(string name, StepStatus status, string msg)
        {
            StepName = name;
            Status = status;
            Message = msg;
        }
    }

    /// <summary>流程上下文：步骤间共享数据。</summary>
    public class AutomationContext
    {
        private readonly Dictionary<string, object> _data = new();

        /// <summary>写入上下文键值。</summary>
        public void Set(string key, object value) => _data[key] = value;

        /// <summary>读取上下文键值。</summary>
        public T? Get<T>(string key)
        {
            if (_data.TryGetValue(key, out var val) && val is T t)
                return t;
            return default;
        }

        /// <summary>判断键是否存在。</summary>
        public bool ContainsKey(string key) => _data.ContainsKey(key);
    }

    /// <summary>步骤1：连接设备。</summary>
    public class ConnectDeviceStep : AutomationStepBase
    {
        public override string StepName => "连接设备";
        public override string Description => "建立与目标设备的连接";
        private readonly IDevice _device;

        /// <summary>构造连接设备步骤。</summary>
        public ConnectDeviceStep(IDevice device) { _device = device; }

        /// <summary>执行连接设备步骤。</summary>
        public override async Task<StepResult> ExecuteAsync(AutomationContext context)
        {
            SetStatus(StepStatus.Running, "正在连接设备...");
            await _device.ConnectAsync();
            Progress = 100;
            context.Set("DeviceConnected", true);
            SetStatus(StepStatus.Completed, $"{_device.Name} 连接成功");
            return new StepResult { Success = true, Message = $"{_device.Name} 已连接" };
        }
    }

    /// <summary>步骤2：采集数据。</summary>
    public class CollectDataStep : AutomationStepBase
    {
        public override string StepName => "采集数据";
        public override string Description => "从设备读取当前测量值";
        private readonly IDevice _device;

        /// <summary>构造采集数据步骤。</summary>
        public CollectDataStep(IDevice device) { _device = device; }

        /// <summary>执行采集数据步骤。</summary>
        public override async Task<StepResult> ExecuteAsync(AutomationContext context)
        {
            SetStatus(StepStatus.Running, "采集中...");
            var value = await _device.ReadDataAsync();
            context.Set("CollectedValue", value);
            Progress = 100;
            SetStatus(StepStatus.Completed, $"采集到数值 {value:F2}");
            return new StepResult { Success = true, Message = $"采集值={value:F2}" };
        }
    }

    /// <summary>步骤3：条件判断。</summary>
    public class ConditionCheckStep : AutomationStepBase
    {
        public override string StepName => "条件判断";
        public override string Description => "检查采集值是否在阈值范围内";
        private readonly double _threshold;

        /// <summary>构造条件判断步骤。</summary>
        public ConditionCheckStep(double threshold) { _threshold = threshold; }

        /// <summary>执行条件判断步骤。</summary>
        public override async Task<StepResult> ExecuteAsync(AutomationContext context)
        {
            SetStatus(StepStatus.Running, "判断中...");
            await Task.Delay(100);
            var value = context.Get<double>("CollectedValue");
            bool passed = value <= _threshold;
            context.Set("ConditionPassed", passed);
            Progress = 100;

            if (passed)
            {
                SetStatus(StepStatus.Completed, $"条件通过: {value:F2} <= {_threshold}");
                return new StepResult { Success = true, Message = "条件检查通过" };
            }

            SetStatus(StepStatus.Completed, $"条件未通过: {value:F2} > {_threshold}");
            return new StepResult { Success = true, Message = "值超出阈值，将触发报警", ContinueNext = true };
        }
    }
}
