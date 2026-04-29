namespace SmartMES.Modules.MotionControl
{
    /// <summary>插补点（多轴联动目标位置）。</summary>
    public class InterpolationPoint
    {
        public Dictionary<string, double> AxisTargets { get; set; } = new();
        public double FeedRate { get; set; } = 100.0;
    }

    /// <summary>多轴运动控制器。</summary>
    public class MultiAxisController
    {
        private readonly Dictionary<string, AxisController> _axes = new();
        private readonly object _queueLock = new();
        private readonly Queue<InterpolationPoint> _interpolationQueue = new();

        public IReadOnlyDictionary<string, AxisController> Axes => _axes;
        public event EventHandler<string>? MessageLogged;

        /// <summary>添加轴到控制器。</summary>
        public void AddAxis(string name, double velocity = 100, double accel = 500)
        {
            var axis = new AxisController(name) { Velocity = velocity, Acceleration = accel };
            axis.MessageLogged += (_, msg) => MessageLogged?.Invoke(this, msg);
            _axes[name] = axis;
        }

        /// <summary>并行回零所有轴。</summary>
        public async Task HomeAllAsync()
        {
            MessageLogged?.Invoke(this, "[MultiAxis] 所有轴开始回零");
            var tasks = _axes.Values.Select(a => Task.Run(() =>
            {
                a.Home();
                while (a.State == AxisState.Homing)
                    Thread.Sleep(50);
            })).ToArray();

            await Task.WhenAll(tasks);
            MessageLogged?.Invoke(this, "[MultiAxis] 所有轴回零完成");
        }

        /// <summary>执行线性插补。</summary>
        public async Task LinearInterpolateAsync(InterpolationPoint point)
        {
            lock (_queueLock)
                _interpolationQueue.Enqueue(point);

            MessageLogged?.Invoke(this, $"[MultiAxis] 线性插补: {string.Join(", ", point.AxisTargets.Select(kv => $"{kv.Key}={kv.Value:F1}mm"))}");

            var distances = point.AxisTargets.ToDictionary(
                kv => kv.Key,
                kv => Math.Abs(kv.Value - (_axes.TryGetValue(kv.Key, out var ax) ? ax.Position : 0)));

            double totalDist = Math.Sqrt(distances.Values.Sum(d => d * d));
            if (totalDist < 0.001) return;

            foreach (var kv in point.AxisTargets)
            {
                if (_axes.TryGetValue(kv.Key, out var axis))
                {
                    axis.Velocity = distances[kv.Key] / totalDist * point.FeedRate;
                    axis.MoveTo(kv.Value);
                }
            }

            await Task.Run(() =>
            {
                while (_axes.Values.Any(a => a.State == AxisState.Running))
                    Thread.Sleep(20);
            });

            MessageLogged?.Invoke(this, "[MultiAxis] 插补完成");
        }

        /// <summary>紧急停止全部轴。</summary>
        public void EmergencyStop()
        {
            foreach (var axis in _axes.Values)
                axis.Stop();
            MessageLogged?.Invoke(this, "[MultiAxis] 紧急停止！");
        }

        /// <summary>复位全部轴到 Idle。</summary>
        public void ResetAll()
        {
            foreach (var axis in _axes.Values)
            {
                axis.Stop();
                axis.Reset();
            }
            MessageLogged?.Invoke(this, "[MultiAxis] 全轴已复位到Idle");
        }
    }
}
