// ============================================================
// 文件：DeviceStateMachine.cs
// 用途：设备状态机 — 管理设备的完整运行生命周期
// 设计思路：
//   工业设备的灵魂组件 — 所有操作都必须在正确的状态下执行。
//
//   状态转换图：
//   ┌─────────────────────────────────────────────────┐
//   │                    ┌──── Paused ◄──── Auto      │
//   │                    │     │  ▲         ▲  │      │
//   │    Idle ──► Init ──┤     │  │Resume   │  │      │
//   │     ▲              │     ▼  │    Start│  │Stop  │
//   │     │              └──► Manual      ──┘  │      │
//   │     │                                    ▼      │
//   │     └────────────── Reset ◄── Alarm ◄───┘      │
//   │     │                                           │
//   │     └──────── EStop ◄────── (任何状态)          │
//   └─────────────────────────────────────────────────┘
//
//   守卫条件确保状态转换的安全性（如：报警未清除时不能进入Auto）。
// ============================================================

using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Events;
using SmartSemiCon.Domain.Interfaces;

namespace SmartSemiCon.Application.StateMachine
{
    /// <summary>
    /// 状态转换规则。
    /// </summary>
    public class StateTransitionRule
    {
        /// <summary>来源状态</summary>
        public DeviceState From { get; init; }

        /// <summary>目标状态</summary>
        public DeviceState To { get; init; }

        /// <summary>触发命令</summary>
        public string Trigger { get; init; } = string.Empty;

        /// <summary>守卫条件 — 返回false则拒绝转换</summary>
        public Func<bool>? Guard { get; init; }

        /// <summary>转换动作 — 转换成功后执行</summary>
        public Func<Task>? OnTransition { get; init; }
    }

    /// <summary>
    /// 设备状态机 — 管理设备生命周期状态的核心组件。
    /// 所有状态转换都是受控的，必须满足守卫条件。
    /// </summary>
    public class DeviceStateMachine
    {
        private readonly List<StateTransitionRule> _rules = new();
        private readonly IEventBus _eventBus;
        private readonly IAlarmService _alarmService;
        private readonly object _lock = new();

        /// <summary>当前状态</summary>
        public DeviceState CurrentState { get; private set; } = DeviceState.Idle;

        /// <summary>状态变更事件</summary>
        public event EventHandler<(DeviceState From, DeviceState To, string Trigger)>? StateChanged;

        public DeviceStateMachine(IEventBus eventBus, IAlarmService alarmService)
        {
            _eventBus = eventBus;
            _alarmService = alarmService;
            BuildStandardTransitions();
        }

        /// <summary>
        /// 构建标准工业设备状态转换规则。
        /// </summary>
        private void BuildStandardTransitions()
        {
            // Idle → Init（初始化，回原点等）
            AddRule(DeviceState.Idle, DeviceState.Init, "Initialize",
                guard: () => _alarmService.ActiveAlarms.Count == 0);

            // Init → Auto（初始化完成，进入自动运行）
            AddRule(DeviceState.Init, DeviceState.Auto, "StartAuto");

            // Init → Manual（初始化完成，进入手动模式）
            AddRule(DeviceState.Init, DeviceState.Manual, "StartManual");

            // Idle → Manual（直接进入手动模式）
            AddRule(DeviceState.Idle, DeviceState.Manual, "StartManual",
                guard: () => _alarmService.ActiveAlarms.Count == 0);

            // Auto → Paused（暂停自动运行）
            AddRule(DeviceState.Auto, DeviceState.Paused, "Pause");

            // Paused → Auto（恢复自动运行）
            AddRule(DeviceState.Paused, DeviceState.Auto, "Resume");

            // Auto → Idle（停止自动运行）
            AddRule(DeviceState.Auto, DeviceState.Idle, "Stop");

            // Manual → Idle（退出手动模式）
            AddRule(DeviceState.Manual, DeviceState.Idle, "Stop");

            // Paused → Idle（暂停状态直接停止）
            AddRule(DeviceState.Paused, DeviceState.Idle, "Stop");

            // 任何运行状态 → Alarm
            AddRule(DeviceState.Auto, DeviceState.Alarm, "Alarm");
            AddRule(DeviceState.Manual, DeviceState.Alarm, "Alarm");
            AddRule(DeviceState.Paused, DeviceState.Alarm, "Alarm");
            AddRule(DeviceState.Init, DeviceState.Alarm, "Alarm");

            // Alarm → Idle（报警复位）
            AddRule(DeviceState.Alarm, DeviceState.Idle, "Reset",
                guard: () => _alarmService.ActiveAlarms.Count == 0);

            // 任何状态 → EmergencyStop（急停）
            foreach (DeviceState state in Enum.GetValues<DeviceState>())
            {
                if (state != DeviceState.EmergencyStop)
                {
                    AddRule(state, DeviceState.EmergencyStop, "EmergencyStop");
                }
            }

            // EmergencyStop → Idle（急停复位）
            AddRule(DeviceState.EmergencyStop, DeviceState.Idle, "Reset");

            // Maintenance模式
            AddRule(DeviceState.Idle, DeviceState.Maintenance, "EnterMaintenance");
            AddRule(DeviceState.Maintenance, DeviceState.Idle, "ExitMaintenance");
        }

        /// <summary>
        /// 添加状态转换规则。
        /// </summary>
        private void AddRule(DeviceState from, DeviceState to, string trigger,
            Func<bool>? guard = null, Func<Task>? onTransition = null)
        {
            _rules.Add(new StateTransitionRule
            {
                From = from, To = to, Trigger = trigger,
                Guard = guard, OnTransition = onTransition
            });
        }

        /// <summary>
        /// 触发状态转换。
        /// </summary>
        /// <param name="trigger">触发命令</param>
        /// <returns>是否成功转换</returns>
        public async Task<bool> FireAsync(string trigger)
        {
            StateTransitionRule? matchedRule;

            lock (_lock)
            {
                matchedRule = _rules.FirstOrDefault(r =>
                    r.From == CurrentState &&
                    r.Trigger == trigger &&
                    (r.Guard == null || r.Guard()));

                if (matchedRule == null) return false;

                var previousState = CurrentState;
                CurrentState = matchedRule.To;

                // 发布状态变更事件
                _eventBus.Publish(new DeviceStateChangedEvent
                {
                    PreviousState = previousState,
                    CurrentState = matchedRule.To,
                    Trigger = trigger,
                    Source = "DeviceStateMachine"
                });

                StateChanged?.Invoke(this, (previousState, matchedRule.To, trigger));
            }

            // 执行转换动作（在锁外执行，避免死锁）
            if (matchedRule.OnTransition != null)
            {
                await matchedRule.OnTransition();
            }

            return true;
        }

        /// <summary>
        /// 强制设置状态（仅用于初始化或紧急场景）。
        /// </summary>
        public void ForceState(DeviceState state)
        {
            lock (_lock)
            {
                var previousState = CurrentState;
                CurrentState = state;
                StateChanged?.Invoke(this, (previousState, state, "[FORCE]"));
            }
        }

        /// <summary>
        /// 获取当前状态下可用的触发命令列表。
        /// </summary>
        public List<string> GetAvailableTriggers()
        {
            lock (_lock)
            {
                return _rules
                    .Where(r => r.From == CurrentState && (r.Guard == null || r.Guard()))
                    .Select(r => r.Trigger)
                    .Distinct()
                    .ToList();
            }
        }
    }
}
