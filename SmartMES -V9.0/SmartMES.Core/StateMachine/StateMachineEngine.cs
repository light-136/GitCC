namespace SmartMES.Core.StateMachine
{
    /// <summary>
    /// 标准工业设备状态枚举。
    /// 覆盖工业设备典型运行生命周期：空闲 → 运行 → 暂停 → 报警 → 复位 → 错误。
    /// </summary>
    public enum MachineState { Idle, Running, Paused, Alarm, Resetting, Error }

    /// <summary>
    /// 状态转换定义：描述从哪个状态、通过哪个触发器、转移到哪个状态。
    /// </summary>
    public class StateTransition
    {
        public MachineState From    { get; init; }
        public MachineState To      { get; init; }
        public string       Trigger { get; init; } = string.Empty;

        /// <summary>转换守卫条件，返回 false 则拒绝本次转换（例如安全互锁未满足）</summary>
        public Func<bool>?  Guard        { get; init; }

        /// <summary>转换动作，状态切换成功后立即执行（例如启动电机、发送命令）</summary>
        public Action?      OnTransition { get; init; }
    }

    /// <summary>
    /// 状态变化事件参数，携带完整的转换上下文信息用于日志和审计。
    /// </summary>
    public class StateChangedArgs : EventArgs
    {
        public MachineState From    { get; init; }
        public MachineState To      { get; init; }
        public string       Trigger { get; init; } = string.Empty;
        public DateTime     Time    { get; init; } = DateTime.Now;
    }

    /// <summary>
    /// 工业状态机接口，定义外部可调用的核心能力。
    /// </summary>
    public interface IStateMachine
    {
        MachineState CurrentState { get; }
        bool Fire(string trigger);
        void AddTransition(StateTransition transition);
        event EventHandler<StateChangedArgs>? StateChanged;
    }

    /// <summary>
    /// 工业状态机引擎。
    /// 统一管理设备/流程/系统的状态转换，支持守卫条件和转换动作。
    /// 所有状态变化通过事件通知 UI 和其他订阅模块，保证解耦。
    /// 线程安全：所有状态读写均通过 _lock 加锁保护。
    /// </summary>
    public class StateMachineEngine : IStateMachine
    {
        private readonly string _name;
        private readonly List<StateTransition> _transitions = new();
        private readonly object _lock = new();

        public MachineState CurrentState { get; private set; } = MachineState.Idle;
        public event EventHandler<StateChangedArgs>? StateChanged;

        /// <summary>
        /// 创建状态机实例，指定名称和初始状态。
        /// </summary>
        /// <param name="name">状态机名称（用于日志标识）</param>
        /// <param name="initialState">初始状态，默认为 Idle</param>
        public StateMachineEngine(string name, MachineState initialState = MachineState.Idle)
        {
            _name = name;
            CurrentState = initialState;
        }

        /// <summary>
        /// 注册一条状态转换规则。
        /// </summary>
        public void AddTransition(StateTransition transition)
        { lock (_lock) _transitions.Add(transition); }

        /// <summary>
        /// 触发状态转换，返回是否成功。
        /// 失败原因：当前状态无匹配转换规则，或守卫条件返回 false。
        /// </summary>
        public bool Fire(string trigger)
        {
            lock (_lock)
            {
                var t = _transitions.FirstOrDefault(x =>
                    x.From == CurrentState &&
                    x.Trigger == trigger &&
                    (x.Guard == null || x.Guard()));

                if (t == null) return false;

                var old = CurrentState;
                CurrentState = t.To;
                t.OnTransition?.Invoke();
                StateChanged?.Invoke(this, new StateChangedArgs
                    { From=old, To=t.To, Trigger=trigger });
                return true;
            }
        }

        /// <summary>
        /// 强制设置状态（仅用于初始化或紧急停止场景）。
        /// 不经过守卫条件，直接覆盖当前状态，慎用。
        /// </summary>
        public void ForceState(MachineState state)
        {
            lock (_lock)
            {
                var old = CurrentState;
                CurrentState = state;
                StateChanged?.Invoke(this, new StateChangedArgs
                    { From=old, To=state, Trigger="[FORCE]" });
            }
        }

        /// <summary>
        /// 构建标准工业设备状态机，预定义常用状态转换图。
        /// 转换图：Idle→Running→Paused→Running / Running→Idle / Running→Alarm→Resetting→Idle / Running→Error→Idle
        /// </summary>
        public static StateMachineEngine BuildStandard(string name)
        {
            var sm = new StateMachineEngine(name);
            sm.AddTransition(new StateTransition { From=MachineState.Idle,      To=MachineState.Running,    Trigger="Start" });
            sm.AddTransition(new StateTransition { From=MachineState.Running,   To=MachineState.Paused,     Trigger="Pause" });
            sm.AddTransition(new StateTransition { From=MachineState.Paused,    To=MachineState.Running,    Trigger="Resume" });
            sm.AddTransition(new StateTransition { From=MachineState.Running,   To=MachineState.Idle,       Trigger="Stop" });
            sm.AddTransition(new StateTransition { From=MachineState.Paused,    To=MachineState.Idle,       Trigger="Stop" });
            sm.AddTransition(new StateTransition { From=MachineState.Running,   To=MachineState.Alarm,      Trigger="Alarm" });
            sm.AddTransition(new StateTransition { From=MachineState.Paused,    To=MachineState.Alarm,      Trigger="Alarm" });
            sm.AddTransition(new StateTransition { From=MachineState.Alarm,     To=MachineState.Resetting,  Trigger="Reset" });
            sm.AddTransition(new StateTransition { From=MachineState.Resetting, To=MachineState.Idle,       Trigger="ResetDone" });
            sm.AddTransition(new StateTransition { From=MachineState.Running,   To=MachineState.Error,      Trigger="Error" });
            sm.AddTransition(new StateTransition { From=MachineState.Error,     To=MachineState.Idle,       Trigger="Clear" });
            return sm;
        }
    }
}
