// ============================================================
// 文件：ElectronicGearing.cs
// 用途：电子齿轮与电子凸轮 — 主从轴同步控制
// 设计思路：
//   电子齿轮：从轴实时跟踪主轴位置，按比例同步运动
//   电子凸轮：通过凸轮表定义主从轴的非线性对应关系
//   两者都使用 10ms 周期的后台线程实现实时跟踪
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 电子齿轮 — 从轴按比例跟踪主轴运动。
    /// 从轴目标位置 = 主轴位置 × 齿轮比 + 相位偏移。
    /// </summary>
    public class ElectronicGearing : IDisposable
    {
        private AxisController? _master;
        private AxisController? _slave;
        private CancellationTokenSource? _cts;
        private Thread? _trackingThread;
        private readonly object _lock = new();

        /// <summary>是否已启用。</summary>
        public bool IsEnabled { get; private set; }

        /// <summary>齿轮比（运行时可调）。</summary>
        public double Ratio { get; set; } = 1.0;

        /// <summary>相位偏移（mm）。</summary>
        public double PhaseOffset { get; set; }

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 启用电子齿轮，从轴开始跟踪主轴。
        /// </summary>
        public void Enable(AxisController master, AxisController slave,
                           double ratio = 1.0, double phaseOffset = 0.0)
        {
            lock (_lock)
            {
                if (IsEnabled) Disable();

                _master = master;
                _slave = slave;
                Ratio = ratio;
                PhaseOffset = phaseOffset;
                IsEnabled = true;

                _cts = new CancellationTokenSource();
                _trackingThread = new Thread(() => TrackingLoop(_cts.Token))
                {
                    IsBackground = true,
                    Name = $"EGear-{master.AxisName}->{slave.AxisName}"
                };
                _trackingThread.Start();

                MessageLogged?.Invoke(this,
                    $"[电子齿轮] 启用：{master.AxisName} → {slave.AxisName}，比例={ratio:F3}");
            }
        }

        /// <summary>
        /// 禁用电子齿轮。
        /// </summary>
        public void Disable()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                IsEnabled = false;
                MessageLogged?.Invoke(this, "[电子齿轮] 已禁用");
            }
        }

        /// <summary>
        /// 10ms 周期跟踪循环 — 读取主轴位置，计算从轴目标。
        /// </summary>
        private void TrackingLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_master != null && _slave != null)
                    {
                        // 计算从轴目标：主轴位置 × 比例 + 偏移
                        double masterPos = _master.Position;
                        double slaveTarget = masterPos * Ratio + PhaseOffset;

                        // 仅当从轴空闲时才发出移动指令
                        if (_slave.State == AxisState.Idle)
                        {
                            _slave.Velocity = Math.Abs(Ratio) * _master.Velocity + 50;
                            _slave.MoveTo(slaveTarget);
                        }
                    }

                    Thread.Sleep(10); // 10ms 周期
                }
                catch (OperationCanceledException) { break; }
                catch { /* 忽略跟踪异常，继续循环 */ }
            }
        }

        public void Dispose()
        {
            Disable();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// 电子凸轮 — 通过凸轮表定义主从轴的非线性对应关系。
    /// 凸轮表中的点按主轴位置升序排列，中间值通过线性插值计算。
    /// </summary>
    public class ElectronicCamming : IDisposable
    {
        private AxisController? _master;
        private AxisController? _slave;
        private CamProfile? _profile;
        private CancellationTokenSource? _cts;
        private Thread? _trackingThread;
        private readonly object _lock = new();

        /// <summary>是否已启用。</summary>
        public bool IsEnabled { get; private set; }

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 启用电子凸轮。
        /// </summary>
        public void Enable(AxisController master, AxisController slave, CamProfile profile)
        {
            lock (_lock)
            {
                if (IsEnabled) Disable();
                if (profile.Points.Count < 2)
                    throw new ArgumentException("凸轮表至少需要2个点");

                _master = master;
                _slave = slave;
                _profile = profile;
                IsEnabled = true;

                _cts = new CancellationTokenSource();
                _trackingThread = new Thread(() => CamTrackingLoop(_cts.Token))
                {
                    IsBackground = true,
                    Name = $"ECam-{master.AxisName}->{slave.AxisName}"
                };
                _trackingThread.Start();

                MessageLogged?.Invoke(this,
                    $"[电子凸轮] 启用：{master.AxisName} → {slave.AxisName}，{profile.Points.Count}个凸轮点");
            }
        }

        /// <summary>
        /// 禁用电子凸轮。
        /// </summary>
        public void Disable()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                IsEnabled = false;
                MessageLogged?.Invoke(this, "[电子凸轮] 已禁用");
            }
        }

        /// <summary>
        /// 凸轮跟踪循环 — 查找凸轮表，插值计算从轴目标位置。
        /// </summary>
        private void CamTrackingLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_master != null && _slave != null && _profile != null)
                    {
                        double masterPos = _master.Position;

                        // 周期性凸轮：对主轴位置取模
                        if (_profile.IsCyclic && _profile.CycleLength > 0)
                            masterPos = masterPos % _profile.CycleLength;

                        // 在凸轮表中查找并插值
                        double slaveTarget = InterpolateCam(masterPos);

                        if (_slave.State == AxisState.Idle)
                        {
                            _slave.Velocity = 200;
                            _slave.MoveTo(slaveTarget);
                        }
                    }

                    Thread.Sleep(10);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        /// <summary>
        /// 凸轮表线性插值 — 根据主轴位置查找从轴目标。
        /// </summary>
        private double InterpolateCam(double masterPos)
        {
            var points = _profile!.Points;

            // 超出范围则钳位到端点
            if (masterPos <= points[0].MasterPosition)
                return points[0].SlavePosition;
            if (masterPos >= points[^1].MasterPosition)
                return points[^1].SlavePosition;

            // 二分查找所在区间
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (masterPos >= points[i].MasterPosition &&
                    masterPos <= points[i + 1].MasterPosition)
                {
                    // 线性插值
                    double range = points[i + 1].MasterPosition - points[i].MasterPosition;
                    if (range < 0.0001) return points[i].SlavePosition;

                    double t = (masterPos - points[i].MasterPosition) / range;
                    return points[i].SlavePosition +
                           t * (points[i + 1].SlavePosition - points[i].SlavePosition);
                }
            }

            return points[^1].SlavePosition;
        }

        public void Dispose()
        {
            Disable();
            _cts?.Dispose();
        }
    }
}
