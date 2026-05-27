// ============================================================
// 文件：DeviceStateMachine.cs
// 层次：应用层 (Application Layer) — 设备状态机
// 职责：
//   管理设备全局运行状态（空闲/初始化/就绪/运行/暂停/停止/故障/维护/急停），
//   控制合法状态转换，发布状态变更事件，保证系统状态一致性。
// 设计思路：
//   有限状态机（FSM）模式：预定义所有合法转换路径，非法转换立即拒绝并告警。
//   线程安全：使用 SemaphoreSlim 保证同一时刻只有一个转换在执行。
//   状态转换回调：允许上层注册 OnEnter/OnExit 回调，用于触发硬件动作。
// ============================================================

using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.Events;
using SmartIndustry.Domain.Interfaces;

namespace SmartIndustry.Application.StateMachine
{
    /// <summary>
    /// 状态转换请求结果
    /// </summary>
    public class StateTransitionResult
    {
        public bool IsSuccess { get; init; }
        public DeviceState PreviousState { get; init; }
        public DeviceState CurrentState { get; init; }
        public string? ErrorMessage { get; init; }

        public static StateTransitionResult Success(DeviceState from, DeviceState to)
            => new() { IsSuccess = true, PreviousState = from, CurrentState = to };

        public static StateTransitionResult Failure(DeviceState current, string reason)
            => new() { IsSuccess = false, PreviousState = current, CurrentState = current, ErrorMessage = reason };
    }

    /// <summary>
    /// 设备状态机 — 管理设备全局运行状态的有限状态机
    /// </summary>
    public class DeviceStateMachine
    {
        private readonly IEventBus _eventBus;
        private readonly ILogService _logService;
        private readonly SemaphoreSlim _transitionLock = new(1, 1);

        private DeviceState _currentState = DeviceState.Idle;

        /// <summary>当前设备状态</summary>
        public DeviceState CurrentState => _currentState;

        /// <summary>状态变更事件（供 UI 绑定）</summary>
        public event Action<DeviceState, DeviceState>? StateChanged;

        // 合法状态转换表：Key=当前状态，Value=允许转换到的目标状态集合
        private static readonly Dictionary<DeviceState, HashSet<DeviceState>> _validTransitions = new()
        {
            [DeviceState.Idle] = new() { DeviceState.Initializing },
            [DeviceState.Initializing] = new() { DeviceState.Ready, DeviceState.Faulted },
            [DeviceState.Ready] = new() { DeviceState.Running, DeviceState.Maintenance, DeviceState.Idle },
            [DeviceState.Running] = new() { DeviceState.Paused, DeviceState.Stopping, DeviceState.Faulted, DeviceState.EmergencyStop },
            [DeviceState.Paused] = new() { DeviceState.Running, DeviceState.Stopping, DeviceState.EmergencyStop },
            [DeviceState.Stopping] = new() { DeviceState.Ready, DeviceState.Faulted },
            [DeviceState.Faulted] = new() { DeviceState.Idle, DeviceState.EmergencyStop },
            [DeviceState.Maintenance] = new() { DeviceState.Ready, DeviceState.Idle },
            [DeviceState.EmergencyStop] = new() { DeviceState.Idle }
        };

        // 状态进入/退出回调
        private readonly Dictionary<DeviceState, List<Func<Task>>> _onEnterCallbacks = new();
        private readonly Dictionary<DeviceState, List<Func<Task>>> _onExitCallbacks = new();

        public DeviceStateMachine(IEventBus eventBus, ILogService logService)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        /// <summary>
        /// 尝试执行状态转换
        /// </summary>
        /// <param name="targetState">目标状态</param>
        /// <param name="reason">转换原因（日志用）</param>
        /// <returns>转换结果</returns>
        public async Task<StateTransitionResult> TransitionToAsync(DeviceState targetState, string? reason = null)
        {
            await _transitionLock.WaitAsync();
            try
            {
                var previousState = _currentState;

                // 检查是否为合法转换
                if (!IsTransitionValid(previousState, targetState))
                {
                    var errorMsg = $"非法状态转换：{previousState} → {targetState}";
                    _logService.Warning("StateMachine", errorMsg);
                    return StateTransitionResult.Failure(previousState, errorMsg);
                }

                // 执行退出回调
                await ExecuteCallbacksAsync(_onExitCallbacks, previousState);

                // 执行状态变更
                _currentState = targetState;

                // 执行进入回调
                await ExecuteCallbacksAsync(_onEnterCallbacks, targetState);

                // 发布领域事件
                var domainEvent = new DeviceStateChangedEvent(previousState, targetState, reason);
                await _eventBus.PublishAsync(domainEvent);

                // 记录日志
                _logService.Info("StateMachine", $"状态转换：{previousState} → {targetState}，原因：{reason ?? "无"}");

                // 触发本地事件
                StateChanged?.Invoke(previousState, targetState);

                return StateTransitionResult.Success(previousState, targetState);
            }
            finally
            {
                _transitionLock.Release();
            }
        }

        /// <summary>
        /// 检查从当前状态到目标状态的转换是否合法
        /// </summary>
        public bool IsTransitionValid(DeviceState from, DeviceState to)
        {
            return _validTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
        }

        /// <summary>
        /// 获取当前状态下允许的目标状态列表
        /// </summary>
        public IReadOnlyCollection<DeviceState> GetAllowedTransitions()
        {
            return _validTransitions.TryGetValue(_currentState, out var allowed)
                ? allowed.ToList().AsReadOnly()
                : Array.Empty<DeviceState>();
        }

        /// <summary>
        /// 注册状态进入回调
        /// </summary>
        public void OnEnter(DeviceState state, Func<Task> callback)
        {
            if (!_onEnterCallbacks.ContainsKey(state))
                _onEnterCallbacks[state] = new List<Func<Task>>();
            _onEnterCallbacks[state].Add(callback);
        }

        /// <summary>
        /// 注册状态退出回调
        /// </summary>
        public void OnExit(DeviceState state, Func<Task> callback)
        {
            if (!_onExitCallbacks.ContainsKey(state))
                _onExitCallbacks[state] = new List<Func<Task>>();
            _onExitCallbacks[state].Add(callback);
        }

        /// <summary>
        /// 强制重置状态（仅在系统恢复场景使用）
        /// </summary>
        public void ForceReset()
        {
            _currentState = DeviceState.Idle;
            _logService.Warning("StateMachine", "状态机被强制重置为 Idle");
        }

        private async Task ExecuteCallbacksAsync(Dictionary<DeviceState, List<Func<Task>>> callbacks, DeviceState state)
        {
            if (callbacks.TryGetValue(state, out var callbackList))
            {
                foreach (var callback in callbackList)
                {
                    try
                    {
                        await callback();
                    }
                    catch (Exception ex)
                    {
                        _logService.Error("StateMachine", $"状态 {state} 回调执行异常：{ex.Message}", ex);
                    }
                }
            }
        }
    }
}
