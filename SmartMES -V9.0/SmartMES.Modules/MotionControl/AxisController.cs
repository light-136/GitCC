using System.Threading;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 单轴运行状态。
    /// </summary>
    public enum AxisState
    {
        Idle,
        Homing,
        Running,
        Paused,
        Error
    }

    /// <summary>
    /// 轴状态变化事件参数。
    /// </summary>
    public class AxisStateChangedArgs : EventArgs
    {
        public string AxisName { get; }
        public AxisState OldState { get; }
        public AxisState NewState { get; }

        /// <summary>
        /// 创建轴状态变化事件参数。
        /// </summary>
        public AxisStateChangedArgs(string name, AxisState old, AxisState @new)
        {
            AxisName = name;
            OldState = old;
            NewState = @new;
        }
    }

    /// <summary>
    /// 单轴控制器，负责单轴的状态切换、运动执行、暂停恢复与错误复位。
    /// </summary>
    public class AxisController
    {
        public string AxisName { get; }

        private AxisState _state = AxisState.Idle;
        private readonly object _stateLock = new();
        public AxisState State { get { lock (_stateLock) return _state; } }

        private double _position = 0.0;
        private double _targetPosition = 0.0;
        private readonly object _posLock = new();
        public double Position { get { lock (_posLock) return _position; } }
        public double TargetPosition { get { lock (_posLock) return _targetPosition; } }

        /// <summary>当前运动速度，单位 mm/s。</summary>
        public double Velocity { get; set; } = 100.0;

        /// <summary>当前运动加速度，单位 mm/s²。</summary>
        public double Acceleration { get; set; } = 500.0;

        private CancellationTokenSource _cts = new();
        private readonly ManualResetEventSlim _resumeEvent = new(true);

        public event EventHandler<AxisStateChangedArgs>? StateChanged;
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 创建单轴控制器实例。
        /// </summary>
        public AxisController(string axisName)
        {
            AxisName = axisName;
        }

        /// <summary>
        /// 发起回零动作，仅在空闲状态下允许执行。
        /// </summary>
        public bool Home()
        {
            lock (_stateLock)
            {
                if (_state != AxisState.Idle) return false;
                TransitionTo(AxisState.Homing);
            }

            StartMotionThread(ExecuteHoming);
            return true;
        }

        /// <summary>
        /// 发起绝对位置移动，仅在空闲状态下允许执行。
        /// 注意：先在 _stateLock 内完成状态检查和切换，再单独更新目标位置，
        /// 避免 _stateLock 内嵌套 _posLock 造成死锁隐患。
        /// </summary>
        public bool MoveTo(double target)
        {
            lock (_stateLock)
            {
                if (_state != AxisState.Idle) return false;
                TransitionTo(AxisState.Running);
            }

            // 状态切换完成后再更新目标位置，两个锁不再嵌套
            lock (_posLock)
            {
                _targetPosition = target;
            }

            StartMotionThread(() => ExecuteMove(target));
            return true;
        }

        /// <summary>
        /// 暂停运行中的轴。
        /// </summary>
        public bool Pause()
        {
            lock (_stateLock)
            {
                if (_state != AxisState.Running) return false;
                _resumeEvent.Reset();
                TransitionTo(AxisState.Paused);
                return true;
            }
        }

        /// <summary>
        /// 恢复已暂停的轴继续运行。
        /// </summary>
        public bool Resume()
        {
            lock (_stateLock)
            {
                if (_state != AxisState.Paused) return false;
                TransitionTo(AxisState.Running);
                _resumeEvent.Set();
                return true;
            }
        }

        /// <summary>
        /// 停止当前运动，并将状态切回空闲。
        /// </summary>
        public bool Stop()
        {
            _cts.Cancel();
            _resumeEvent.Set();
            lock (_stateLock)
            {
                TransitionTo(AxisState.Idle);
            }
            _cts = new CancellationTokenSource();
            return true;
        }

        /// <summary>
        /// 将当前轴置为错误状态，并记录错误信息。
        /// </summary>
        public bool SetError(string reason)
        {
            _cts.Cancel();
            _resumeEvent.Set();
            lock (_stateLock)
            {
                TransitionTo(AxisState.Error);
            }
            MessageLogged?.Invoke(this, $"[{AxisName}] ERROR: {reason}");
            return true;
        }

        /// <summary>
        /// 从错误状态复位回空闲。
        /// </summary>
        public bool Reset()
        {
            lock (_stateLock)
            {
                if (_state != AxisState.Error) return false;
                _cts = new CancellationTokenSource();
                _resumeEvent.Set();
                TransitionTo(AxisState.Idle);
                return true;
            }
        }

        /// <summary>
        /// 切换状态并广播状态变化日志。
        /// </summary>
        private void TransitionTo(AxisState newState)
        {
            var old = _state;
            _state = newState;
            StateChanged?.Invoke(this, new AxisStateChangedArgs(AxisName, old, newState));
            MessageLogged?.Invoke(this, $"[{AxisName}] {old} -> {newState}");
        }

        /// <summary>
        /// 在后台线程中执行运动任务，统一处理取消与异常。
        /// </summary>
        private void StartMotionThread(Action action)
        {
            var t = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    SetError(ex.Message);
                }
            })
            {
                IsBackground = true,
                Name = $"Axis-{AxisName}"
            };
            t.Start();
        }

        /// <summary>
        /// 执行回零动作，将轴移动到原点。
        /// </summary>
        private void ExecuteHoming()
        {
            MessageLogged?.Invoke(this, $"[{AxisName}] 开始回零...");
            MoveToPosition(0.0);
            lock (_stateLock)
            {
                TransitionTo(AxisState.Idle);
            }
            MessageLogged?.Invoke(this, $"[{AxisName}] 回零完成");
        }

        /// <summary>
        /// 执行绝对位置移动。
        /// </summary>
        private void ExecuteMove(double target)
        {
            MessageLogged?.Invoke(this, $"[{AxisName}] 移动至 {target:F2}mm");
            MoveToPosition(target);
            lock (_stateLock)
            {
                if (_state == AxisState.Running)
                    TransitionTo(AxisState.Idle);
            }
            MessageLogged?.Invoke(this, $"[{AxisName}] 到位: {Position:F2}mm");
        }

        /// <summary>
        /// 按梯形速度曲线模拟轴运动，每 10ms 刷新一次位置。
        /// </summary>
        private void MoveToPosition(double target)
        {
            const int intervalMs = 10;
            const double intervalSec = intervalMs / 1000.0;

            double currentVel = 0;
            double current;
            lock (_posLock)
            {
                current = _position;
            }

            double dir = target > current ? 1 : -1;
            double distance = Math.Abs(target - current);
            if (distance < 0.001) return;

            while (!_cts.IsCancellationRequested)
            {
                _resumeEvent.Wait(_cts.Token);
                if (_cts.IsCancellationRequested) break;

                lock (_posLock)
                {
                    double remaining = Math.Abs(target - _position);
                    if (remaining < 0.01)
                    {
                        _position = target;
                        break;
                    }

                    double stopDist = currentVel * currentVel / (2 * Acceleration);
                    if (remaining <= stopDist + 0.1)
                        currentVel = Math.Max(0, currentVel - Acceleration * intervalSec);
                    else
                        currentVel = Math.Min(Velocity, currentVel + Acceleration * intervalSec);

                    double step = dir * currentVel * intervalSec;
                    if (Math.Abs(step) > remaining) step = dir * remaining;
                    _position += step;
                }

                Thread.Sleep(intervalMs);
            }
        }
    }
}
