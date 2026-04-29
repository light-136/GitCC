namespace SmartMES.Core.Safety
{
    /// <summary>互锁条件类型。</summary>
    public enum InterlockType
    {
        PreCondition,
        PostCondition
    }

    /// <summary>互锁条件定义。</summary>
    public class InterlockCondition
    {
        /// <summary>条件名称。</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>条件描述。</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>条件类型（前置/后置）。</summary>
        public InterlockType Type { get; init; }

        /// <summary>条件检查函数，返回 true 表示通过。</summary>
        public Func<bool> Check { get; init; } = () => true;

        /// <summary>条件是否启用。</summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>安全事件参数。</summary>
    public class SafetyEventArgs : EventArgs
    {
        /// <summary>事件来源。</summary>
        public string Source { get; init; } = string.Empty;

        /// <summary>事件消息。</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>是否为急停事件。</summary>
        public bool IsEStop { get; init; }

        /// <summary>事件时间。</summary>
        public DateTime Time { get; init; } = DateTime.Now;
    }

    /// <summary>安全服务接口。</summary>
    public interface ISafetyService
    {
        /// <summary>当前是否处于急停状态。</summary>
        bool IsEStopActive { get; }

        /// <summary>检查某操作当前是否允许执行。</summary>
        bool IsSafeToOperate(string operationName);

        /// <summary>触发急停。</summary>
        void TriggerEStop(string reason);

        /// <summary>复位急停。</summary>
        void ResetEStop();

        /// <summary>添加互锁条件。</summary>
        void AddInterlock(string operationName, InterlockCondition condition);

        /// <summary>急停触发事件。</summary>
        event EventHandler<SafetyEventArgs>? EStopTriggered;

        /// <summary>互锁拦截事件。</summary>
        event EventHandler<SafetyEventArgs>? InterlockBlocked;
    }

    /// <summary>
    /// 安全与互锁服务。
    /// 急停优先级最高，所有操作在执行前均应通过互锁检查。
    /// </summary>
    public class SafetyService : ISafetyService
    {
        private volatile bool _eStop = false;
        private readonly Dictionary<string, List<InterlockCondition>> _interlocks = new();
        private readonly object _lock = new();

        /// <summary>当前急停状态。</summary>
        public bool IsEStopActive => _eStop;

        /// <summary>急停触发事件。</summary>
        public event EventHandler<SafetyEventArgs>? EStopTriggered;

        /// <summary>互锁拦截事件。</summary>
        public event EventHandler<SafetyEventArgs>? InterlockBlocked;

        /// <summary>仿真安全门状态。</summary>
        public bool DoorClosed { get; set; } = true;

        /// <summary>仿真气压状态。</summary>
        public bool AirPressureOk { get; set; } = true;

        /// <summary>仿真安全光栅状态。</summary>
        public bool SafetyLightOk { get; set; } = true;

        /// <summary>仿真急停按钮状态。</summary>
        public bool EmergencyBtnOk { get; set; } = true;

        /// <summary>构造函数：初始化默认互锁规则。</summary>
        public SafetyService()
        {
            AddInterlock("*", new InterlockCondition
            {
                Name = "急停检查",
                Type = InterlockType.PreCondition,
                Check = () => !_eStop,
                Description = "急停未复位时禁止所有操作"
            });

            AddInterlock("StartDevice", new InterlockCondition
            {
                Name = "安全门检查",
                Type = InterlockType.PreCondition,
                Check = () => DoorClosed,
                Description = "安全门关闭后才允许启动设备"
            });

            AddInterlock("StartDevice", new InterlockCondition
            {
                Name = "气压检查",
                Type = InterlockType.PreCondition,
                Check = () => AirPressureOk,
                Description = "气压不足时禁止启动设备"
            });

            AddInterlock("ExecuteMotion", new InterlockCondition
            {
                Name = "急停按钮检查",
                Type = InterlockType.PreCondition,
                Check = () => EmergencyBtnOk && !_eStop,
                Description = "急停按钮触发时禁止运动"
            });
        }

        /// <summary>执行安全检查，返回操作是否可执行。</summary>
        public bool IsSafeToOperate(string operationName)
        {
            lock (_lock)
            {
                if (_interlocks.TryGetValue("*", out var globalConds))
                {
                    foreach (var c in globalConds.Where(x => x.IsActive))
                    {
                        if (!c.Check())
                        {
                            InterlockBlocked?.Invoke(this, new SafetyEventArgs
                            {
                                Source = c.Name,
                                Message = $"[{operationName}] 被互锁：{c.Description}"
                            });
                            return false;
                        }
                    }
                }

                if (_interlocks.TryGetValue(operationName, out var opConds))
                {
                    foreach (var c in opConds.Where(x => x.IsActive))
                    {
                        if (!c.Check())
                        {
                            InterlockBlocked?.Invoke(this, new SafetyEventArgs
                            {
                                Source = c.Name,
                                Message = $"[{operationName}] 被互锁：{c.Description}"
                            });
                            return false;
                        }
                    }
                }

                return true;
            }
        }

        /// <summary>触发急停并广播急停事件。</summary>
        public void TriggerEStop(string reason)
        {
            _eStop = true;
            EStopTriggered?.Invoke(this, new SafetyEventArgs
            {
                Source = "EStop",
                Message = $"急停触发：{reason}",
                IsEStop = true
            });
        }

        /// <summary>复位急停（需满足基本安全条件）。</summary>
        public void ResetEStop()
        {
            if (DoorClosed && AirPressureOk && SafetyLightOk)
                _eStop = false;
        }

        /// <summary>为指定操作添加互锁条件。</summary>
        public void AddInterlock(string operationName, InterlockCondition condition)
        {
            lock (_lock)
            {
                if (!_interlocks.ContainsKey(operationName))
                    _interlocks[operationName] = new List<InterlockCondition>();

                _interlocks[operationName].Add(condition);
            }
        }
    }
}
