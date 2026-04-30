// ============================================================
// 文件：PerformanceMonitor.cs
// 用途：30轴运动控制系统 — 性能监控器
// 设计思路：
//   本模块负责对所有已注册轴进行周期性采样，记录轴的位置、
//   跟随误差、速度、电机负载等性能指标。通过后台线程以固定
//   间隔（默认 100ms）采集各轴快照并存入历史缓冲区，同时
//   测量每次扫描周期的执行时间，为系统性能分析提供依据。
//
//   关键设计要点：
//   1. 线程安全 — 所有共享数据（_history、_cycleTimes 等）
//      通过 lock 保护，防止采样线程与查询线程并发冲突。
//   2. 历史缓冲限制 — 每轴最多保留 _maxHistoryPerAxis 条快照，
//      超出时移除最早的记录，避免内存无限增长。
//   3. 周期时间跟踪 — 使用 Stopwatch 精确测量每次扫描周期，
//      最近 100 次周期时间用于计算平均/最大周期时间。
//   4. 事件通知 — 通过 SnapshotTaken 事件通知外部模块每次采样
//      结果，通过 MessageLogged 事件输出诊断日志。
//   5. 碰撞警告计数 — 由外部碰撞检测器调用 IncrementCollisionWarning
//      递增，在系统报告中统一呈现。
// ============================================================

using SmartMES.Core.Models;
using System.Diagnostics;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 性能监控器 — 对已注册的运动轴进行周期性采样并记录性能指标。
    /// 支持后台自动采样、历史查询、系统性能报告生成等功能。
    /// 实现 IDisposable 以确保后台线程正确释放。
    /// </summary>
    public class PerformanceMonitor : IDisposable
    {
        // ==================== 字段定义 ====================

        /// <summary>已注册的轴控制器字典，键为轴名称。</summary>
        private readonly Dictionary<string, AxisController> _axes = new();

        /// <summary>每个轴的性能快照历史记录，键为轴名称。</summary>
        private readonly Dictionary<string, List<AxisPerformanceSnapshot>> _history = new();

        /// <summary>每轴最大保留快照数量，防止内存无限增长。</summary>
        private readonly int _maxHistoryPerAxis = 1000;

        /// <summary>采样间隔（毫秒），默认 100ms 即 10Hz 采样率。</summary>
        private readonly int _sampleIntervalMs = 100;

        /// <summary>标记当前是否正在录制（后台采样线程运行中）。</summary>
        private bool _recording;

        /// <summary>用于取消后台采样线程的令牌源。</summary>
        private CancellationTokenSource? _cts;

        /// <summary>高精度计时器，用于测量每次扫描周期的执行时间。</summary>
        private readonly Stopwatch _cycleStopwatch = new();

        /// <summary>最近的扫描周期时间列表（毫秒），最多保留 100 条。</summary>
        private readonly List<double> _cycleTimes = new();

        /// <summary>碰撞警告累计次数，由外部碰撞检测模块递增。</summary>
        private int _collisionWarningCount;

        /// <summary>同步锁对象，保护 _history、_cycleTimes 等共享数据。</summary>
        private readonly object _lock = new();

        /// <summary>是否已释放资源。</summary>
        private bool _disposed;

        // ==================== 事件定义 ====================

        /// <summary>日志消息事件，用于输出监控器的诊断信息。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>快照采集事件，每次对单个轴采样后触发。</summary>
        public event EventHandler<AxisPerformanceSnapshot>? SnapshotTaken;

        // ==================== 构造函数 ====================

        /// <summary>
        /// 创建性能监控器实例，使用默认参数。
        /// </summary>
        public PerformanceMonitor() { }

        /// <summary>
        /// 创建性能监控器实例，允许自定义采样间隔和历史缓冲大小。
        /// </summary>
        /// <param name="sampleIntervalMs">采样间隔（毫秒），建议 50~500。</param>
        /// <param name="maxHistoryPerAxis">每轴最大历史快照数量。</param>
        public PerformanceMonitor(int sampleIntervalMs, int maxHistoryPerAxis)
        {
            _sampleIntervalMs = sampleIntervalMs > 0 ? sampleIntervalMs : 100;
            _maxHistoryPerAxis = maxHistoryPerAxis > 0 ? maxHistoryPerAxis : 1000;
        }

        // ==================== 公共方法 ====================

        /// <summary>
        /// 注册一个轴控制器到监控器中。
        /// 注册后该轴将参与后续的周期性采样。
        /// 若轴名称已存在则跳过注册。
        /// </summary>
        /// <param name="axis">要注册的轴控制器实例。</param>
        public void RegisterAxis(AxisController axis)
        {
            if (axis == null) throw new ArgumentNullException(nameof(axis));

            lock (_lock)
            {
                // 避免重复注册同名轴
                if (_axes.ContainsKey(axis.AxisName))
                {
                    Log($"轴 '{axis.AxisName}' 已注册，跳过重复注册");
                    return;
                }

                _axes[axis.AxisName] = axis;
                _history[axis.AxisName] = new List<AxisPerformanceSnapshot>();
                Log($"轴 '{axis.AxisName}' 已注册到性能监控器");
            }
        }

        /// <summary>
        /// 启动后台采样线程，开始周期性记录所有已注册轴的性能数据。
        /// 若已在录制中则不重复启动。
        /// </summary>
        public void StartRecording()
        {
            lock (_lock)
            {
                if (_recording)
                {
                    Log("采样线程已在运行中，忽略重复启动请求");
                    return;
                }

                _cts = new CancellationTokenSource();
                _recording = true;
            }

            // 在后台线程中执行采样循环
            var thread = new Thread(() => RecordingLoop(_cts.Token))
            {
                IsBackground = true,
                Name = "PerformanceMonitor-Sampler"
            };
            thread.Start();

            Log($"性能采样已启动，采样间隔={_sampleIntervalMs}ms，已注册轴数={_axes.Count}");
        }

        /// <summary>
        /// 停止后台采样线程。
        /// 调用后采样循环将在当前周期结束后退出。
        /// </summary>
        public void StopRecording()
        {
            lock (_lock)
            {
                if (!_recording)
                {
                    Log("采样线程未在运行，忽略停止请求");
                    return;
                }

                _cts?.Cancel();
                _recording = false;
            }

            Log("性能采样已停止");
        }

        /// <summary>
        /// 对单个轴进行一次性能快照采样。
        /// 记录轴的当前位置、目标位置、跟随误差、速度和模拟电机负载。
        /// </summary>
        /// <param name="axis">目标轴控制器。</param>
        /// <returns>该轴的性能快照。</returns>
        public AxisPerformanceSnapshot SampleAxis(AxisController axis)
        {
            if (axis == null) throw new ArgumentNullException(nameof(axis));

            // 读取轴的当前状态
            double position = axis.Position;
            double targetPosition = axis.TargetPosition;
            double velocity = axis.Velocity;

            // 计算跟随误差：目标位置与实际位置之差
            double followingError = targetPosition - position;

            // 模拟电机负载率：基于速度的简化计算模型
            // 速度越高，电机负载越大，上限为 100%
            double motorLoad = Math.Min(100, Math.Abs(velocity) / Math.Max(1, velocity) * 50);

            var snapshot = new AxisPerformanceSnapshot
            {
                AxisName = axis.AxisName,
                Timestamp = DateTime.Now,
                Position = position,
                CommandPosition = targetPosition,
                FollowingError = followingError,
                Velocity = velocity,
                MotorLoad = motorLoad
            };

            // 触发快照采集事件，通知外部订阅者
            SnapshotTaken?.Invoke(this, snapshot);

            return snapshot;
        }

        /// <summary>
        /// 对所有已注册轴进行一次快照采样。
        /// 返回本次采样的所有轴快照列表。
        /// </summary>
        /// <returns>所有轴的性能快照列表。</returns>
        public List<AxisPerformanceSnapshot> SampleAllAxes()
        {
            var snapshots = new List<AxisPerformanceSnapshot>();

            lock (_lock)
            {
                foreach (var kvp in _axes)
                {
                    var snapshot = SampleAxis(kvp.Value);
                    snapshots.Add(snapshot);
                }
            }

            return snapshots;
        }

        /// <summary>
        /// 生成系统性能报告。
        /// 报告包含指定时间窗口内的统计数据：活动轴数、错误轴数、
        /// 最大跟随误差、平均/最大周期时间、碰撞警告次数等。
        /// </summary>
        /// <param name="windowSeconds">统计窗口（秒），默认 60 秒。</param>
        /// <returns>系统性能报告。</returns>
        public SystemPerformanceReport GenerateReport(double windowSeconds = 60)
        {
            var report = new SystemPerformanceReport
            {
                GeneratedAt = DateTime.Now,
                WindowSeconds = windowSeconds
            };

            // 计算时间窗口的起始时间
            DateTime windowStart = DateTime.Now.AddSeconds(-windowSeconds);

            lock (_lock)
            {
                // 统计总轴数
                report.TotalAxes = _axes.Count;

                // 统计活动轴数（状态为 Running）和错误轴数（状态为 Error）
                foreach (var kvp in _axes)
                {
                    var state = kvp.Value.State;
                    if (state == AxisState.Running)
                        report.ActiveAxes++;
                    if (state == AxisState.Error)
                        report.ErrorAxes++;
                }

                // 收集时间窗口内所有轴的快照，并计算最大跟随误差
                double maxFollowingError = 0;

                foreach (var kvp in _history)
                {
                    // 筛选窗口内的快照
                    var windowSnapshots = kvp.Value
                        .Where(s => s.Timestamp >= windowStart)
                        .ToList();

                    // 将窗口内快照加入报告
                    report.AxisSnapshots.AddRange(windowSnapshots);

                    // 计算该轴在窗口内的最大跟随误差
                    foreach (var snap in windowSnapshots)
                    {
                        double absError = Math.Abs(snap.FollowingError);
                        if (absError > maxFollowingError)
                            maxFollowingError = absError;
                    }
                }

                report.MaxFollowingError = maxFollowingError;

                // 计算周期时间统计：平均值和最大值
                if (_cycleTimes.Count > 0)
                {
                    report.AverageCycleTimeMs = _cycleTimes.Average();
                    report.MaxCycleTimeMs = _cycleTimes.Max();
                }

                // 填入碰撞警告次数
                report.CollisionWarnings = _collisionWarningCount;
            }

            Log($"系统性能报告已生成：总轴数={report.TotalAxes}，活动轴={report.ActiveAxes}，" +
                $"错误轴={report.ErrorAxes}，最大跟随误差={report.MaxFollowingError:F4}mm");

            return report;
        }

        /// <summary>
        /// 获取指定轴的历史性能快照。
        /// 返回最近的 maxCount 条记录，按时间从旧到新排列。
        /// </summary>
        /// <param name="axisName">轴名称。</param>
        /// <param name="maxCount">最大返回条数，默认 100。</param>
        /// <returns>该轴的历史快照列表；若轴未注册则返回空列表。</returns>
        public List<AxisPerformanceSnapshot> GetAxisHistory(string axisName, int maxCount = 100)
        {
            lock (_lock)
            {
                if (!_history.ContainsKey(axisName))
                {
                    Log($"查询轴 '{axisName}' 历史失败：轴未注册");
                    return new List<AxisPerformanceSnapshot>();
                }

                var history = _history[axisName];

                // 取最近的 maxCount 条记录
                int startIndex = Math.Max(0, history.Count - maxCount);
                int count = Math.Min(maxCount, history.Count);
                return history.GetRange(startIndex, count);
            }
        }

        /// <summary>
        /// 递增碰撞警告计数器。
        /// 由外部碰撞检测模块在检测到潜在碰撞风险时调用。
        /// </summary>
        public void IncrementCollisionWarning()
        {
            // 使用原子操作递增，保证线程安全
            Interlocked.Increment(ref _collisionWarningCount);
            Log("碰撞警告计数 +1，当前总计=" + _collisionWarningCount);
        }

        /// <summary>
        /// 清除所有轴的历史快照数据和周期时间记录。
        /// 不影响已注册的轴和碰撞计数器。
        /// </summary>
        public void ClearHistory()
        {
            lock (_lock)
            {
                foreach (var kvp in _history)
                {
                    kvp.Value.Clear();
                }
                _cycleTimes.Clear();
            }

            Log("所有历史数据已清除");
        }

        /// <summary>
        /// 释放资源：停止后台采样线程并清理令牌源。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 停止录制线程
            StopRecording();

            // 释放取消令牌源
            _cts?.Dispose();
            _cts = null;

            Log("性能监控器已释放");
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 后台采样循环 — 在独立线程中以固定间隔采集所有轴的性能数据。
        /// 每个周期执行以下步骤：
        ///   1. 启动周期计时
        ///   2. 采集所有轴的快照
        ///   3. 将快照存入各轴的历史缓冲区
        ///   4. 记录本次周期的执行时间
        ///   5. 等待至下一个采样周期
        /// </summary>
        /// <param name="ct">取消令牌，用于优雅退出循环。</param>
        private void RecordingLoop(CancellationToken ct)
        {
            Log("采样循环线程已启动");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 启动周期计时器
                    _cycleStopwatch.Restart();

                    // 采集所有轴的快照
                    var snapshots = SampleAllAxes();

                    // 将快照存入历史缓冲区
                    lock (_lock)
                    {
                        foreach (var snapshot in snapshots)
                        {
                            if (_history.TryGetValue(snapshot.AxisName, out var history))
                            {
                                history.Add(snapshot);

                                // 超出缓冲上限时移除最早的记录
                                while (history.Count > _maxHistoryPerAxis)
                                {
                                    history.RemoveAt(0);
                                }
                            }
                        }

                        // 记录本次周期执行时间
                        _cycleStopwatch.Stop();
                        double cycleMs = _cycleStopwatch.Elapsed.TotalMilliseconds;
                        _cycleTimes.Add(cycleMs);

                        // 周期时间列表最多保留 100 条
                        while (_cycleTimes.Count > 100)
                        {
                            _cycleTimes.RemoveAt(0);
                        }
                    }

                    // 等待至下一个采样周期（减去本次执行时间）
                    int elapsed = (int)_cycleStopwatch.Elapsed.TotalMilliseconds;
                    int waitTime = Math.Max(1, _sampleIntervalMs - elapsed);
                    Thread.Sleep(waitTime);
                }
                catch (OperationCanceledException)
                {
                    // 正常退出：收到取消信号
                    break;
                }
                catch (Exception ex)
                {
                    // 异常处理：记录错误但不中断采样循环
                    Log($"采样循环异常：{ex.Message}");
                    Thread.Sleep(_sampleIntervalMs);
                }
            }

            Log("采样循环线程已退出");
        }

        /// <summary>
        /// 内部日志方法 — 通过 MessageLogged 事件输出诊断信息。
        /// 所有日志消息带有 [PerformanceMonitor] 前缀以便识别来源。
        /// </summary>
        /// <param name="msg">日志消息内容。</param>
        private void Log(string msg)
        {
            MessageLogged?.Invoke(this, $"[PerformanceMonitor] {msg}");
        }
    }
}
