// ============================================================
// 文件：CollisionDetector.cs
// 用途：30轴工业运动控制系统 — 碰撞检测器
//
// 设计思路：
//   在多轴联动系统中，不同轴组可能共享物理空间。当两个轴
//   同时进入预定义的危险重叠区域时，存在机械碰撞风险。
//
//   碰撞检测器通过后台监控线程以固定周期（默认50ms）轮询
//   所有已注册碰撞区域，判断两个轴是否同时处于各自的危险
//   位置范围内。若检测到碰撞风险：
//     - "Warning" 级别：记录事件并触发事件通知
//     - "Critical" 级别：立即停止相关轴的运动
//
//   碰撞判定规则（简化模型）：
//     当 轴1位置 ∈ [Axis1Min, Axis1Max] 且
//        轴2位置 ∈ [Axis2Min, Axis2Max] 时，
//     认为两轴同时进入危险重叠区域，触发碰撞事件。
//     距离计算 = |轴1位置 - 轴2位置|，
//     距离 < MinSafeDistance 判定为 "Critical"，否则为 "Warning"。
//
//   线程安全：
//     使用 lock 保护碰撞区域列表和事件日志的并发访问，
//     确保注册/移除区域与后台监控循环之间不会产生竞态条件。
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 碰撞事件 — 记录一次碰撞检测触发的详细信息。
    /// </summary>
    public class CollisionEvent
    {
        /// <summary>事件发生时间戳。</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>触发碰撞的区域名称。</summary>
        public string ZoneName { get; set; } = string.Empty;

        /// <summary>第一个轴名称。</summary>
        public string Axis1Name { get; set; } = string.Empty;

        /// <summary>第二个轴名称。</summary>
        public string Axis2Name { get; set; } = string.Empty;

        /// <summary>第一个轴的当前位置（mm）。</summary>
        public double Axis1Position { get; set; }

        /// <summary>第二个轴的当前位置（mm）。</summary>
        public double Axis2Position { get; set; }

        /// <summary>两轴之间的距离（mm）。</summary>
        public double Distance { get; set; }

        /// <summary>严重程度："Warning" 或 "Critical"。</summary>
        public string Severity { get; set; } = string.Empty;

        /// <summary>碰撞事件描述信息。</summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 碰撞检测器 — 监控多轴系统中各碰撞区域，防止轴组之间发生机械碰撞。
    /// 通过后台线程周期性扫描所有已启用的碰撞区域，在检测到危险时
    /// 触发事件通知并根据严重程度自动停止相关轴。
    /// </summary>
    public class CollisionDetector : IDisposable
    {
        // ==================== 私有字段 ====================

        /// <summary>已注册的碰撞区域列表。</summary>
        private readonly List<CollisionZone> _zones = new();

        /// <summary>已注册的轴控制器引用（按轴名索引）。</summary>
        private readonly Dictionary<string, AxisController> _axes = new();

        /// <summary>碰撞事件历史记录（最多保留100条）。</summary>
        private readonly List<CollisionEvent> _eventLog = new();

        /// <summary>后台监控是否正在运行。</summary>
        private bool _monitoring;

        /// <summary>后台监控线程的取消令牌源。</summary>
        private CancellationTokenSource? _cts;

        /// <summary>扫描间隔时间（毫秒），默认50ms对应20Hz扫描频率。</summary>
        private int _scanIntervalMs = 50;

        /// <summary>事件日志最大容量。</summary>
        private const int MaxEventLogSize = 100;

        /// <summary>用于保护 _zones 和 _eventLog 的并发锁。</summary>
        private readonly object _lock = new();

        // ==================== 公共事件 ====================

        /// <summary>检测到碰撞时触发的事件。</summary>
        public event EventHandler<CollisionEvent>? CollisionDetected;

        /// <summary>日志消息事件，用于外部订阅监控日志输出。</summary>
        public event EventHandler<string>? MessageLogged;

        // ==================== 公共属性 ====================

        /// <summary>获取或设置扫描间隔（毫秒）。</summary>
        public int ScanIntervalMs
        {
            get => _scanIntervalMs;
            set => _scanIntervalMs = value > 0 ? value : 50;
        }

        /// <summary>后台监控是否正在运行。</summary>
        public bool IsMonitoring => _monitoring;

        // ==================== 轴与区域注册 ====================

        /// <summary>
        /// 注册一个轴控制器，使碰撞检测器可以读取该轴的实时位置。
        /// 若轴名已存在则覆盖旧的引用。
        /// </summary>
        /// <param name="axis">要注册的轴控制器实例。</param>
        public void RegisterAxis(AxisController axis)
        {
            if (axis == null)
                throw new ArgumentNullException(nameof(axis));

            lock (_lock)
            {
                _axes[axis.AxisName] = axis;
            }

            Log($"已注册轴: {axis.AxisName}");
        }

        /// <summary>
        /// 添加碰撞区域。添加前会验证区域中引用的两个轴名是否已注册。
        /// </summary>
        /// <param name="zone">碰撞区域定义。</param>
        /// <exception cref="ArgumentException">当引用的轴未注册时抛出。</exception>
        public void AddZone(CollisionZone zone)
        {
            if (zone == null)
                throw new ArgumentNullException(nameof(zone));

            lock (_lock)
            {
                // 验证轴1是否已注册
                if (!_axes.ContainsKey(zone.Axis1Name))
                    throw new ArgumentException($"轴 '{zone.Axis1Name}' 未注册，无法添加碰撞区域。");

                // 验证轴2是否已注册
                if (!_axes.ContainsKey(zone.Axis2Name))
                    throw new ArgumentException($"轴 '{zone.Axis2Name}' 未注册，无法添加碰撞区域。");

                _zones.Add(zone);
            }

            Log($"已添加碰撞区域: {zone.Name} (轴1={zone.Axis1Name}, 轴2={zone.Axis2Name})");
        }

        /// <summary>
        /// 按名称移除碰撞区域。
        /// </summary>
        /// <param name="zoneName">要移除的区域名称。</param>
        public void RemoveZone(string zoneName)
        {
            lock (_lock)
            {
                int removed = _zones.RemoveAll(z => z.Name == zoneName);
                if (removed > 0)
                    Log($"已移除碰撞区域: {zoneName}");
            }
        }

        /// <summary>
        /// 启用指定碰撞区域。
        /// </summary>
        /// <param name="zoneName">区域名称。</param>
        public void EnableZone(string zoneName)
        {
            lock (_lock)
            {
                var zone = _zones.Find(z => z.Name == zoneName);
                if (zone != null)
                {
                    zone.IsEnabled = true;
                    Log($"已启用碰撞区域: {zoneName}");
                }
            }
        }

        /// <summary>
        /// 禁用指定碰撞区域。
        /// </summary>
        /// <param name="zoneName">区域名称。</param>
        public void DisableZone(string zoneName)
        {
            lock (_lock)
            {
                var zone = _zones.Find(z => z.Name == zoneName);
                if (zone != null)
                {
                    zone.IsEnabled = false;
                    Log($"已禁用碰撞区域: {zoneName}");
                }
            }
        }

        // ==================== 碰撞检测逻辑 ====================

        /// <summary>
        /// 对指定碰撞区域执行一次碰撞检测。
        /// 检测逻辑：判断两个轴是否同时处于各自的危险位置范围内。
        /// 若两轴均在危险范围内，则计算两轴距离并根据安全距离阈值判定严重程度。
        /// </summary>
        /// <param name="zoneName">要检测的区域名称。</param>
        /// <returns>若检测到碰撞则返回碰撞事件，否则返回 null。</returns>
        public CollisionEvent? CheckCollision(string zoneName)
        {
            CollisionZone? zone;
            AxisController? axis1;
            AxisController? axis2;

            lock (_lock)
            {
                // 查找指定区域
                zone = _zones.Find(z => z.Name == zoneName);
                if (zone == null || !zone.IsEnabled)
                    return null;

                // 获取轴控制器引用
                _axes.TryGetValue(zone.Axis1Name, out axis1);
                _axes.TryGetValue(zone.Axis2Name, out axis2);
            }

            // 如果轴引用不存在则无法检测
            if (axis1 == null || axis2 == null)
                return null;

            // 读取两轴当前位置
            double pos1 = axis1.Position;
            double pos2 = axis2.Position;

            // 判断轴1是否处于危险范围内
            bool axis1InZone = pos1 >= zone.Axis1Min && pos1 <= zone.Axis1Max;

            // 判断轴2是否处于危险范围内
            bool axis2InZone = pos2 >= zone.Axis2Min && pos2 <= zone.Axis2Max;

            // 两轴必须同时处于危险范围内才触发碰撞检测
            if (!axis1InZone || !axis2InZone)
                return null;

            // 计算两轴之间的距离
            double distance = Math.Abs(pos1 - pos2);

            // 根据距离与安全距离阈值判定严重程度
            string severity = distance < zone.MinSafeDistance ? "Critical" : "Warning";

            // 构造碰撞事件
            string message = severity == "Critical"
                ? $"严重碰撞风险！区域 [{zone.Name}] 中 {zone.Axis1Name}({pos1:F2}mm) 与 " +
                  $"{zone.Axis2Name}({pos2:F2}mm) 距离 {distance:F2}mm < 安全距离 {zone.MinSafeDistance:F2}mm"
                : $"碰撞预警！区域 [{zone.Name}] 中 {zone.Axis1Name}({pos1:F2}mm) 与 " +
                  $"{zone.Axis2Name}({pos2:F2}mm) 同时进入危险区域，距离 {distance:F2}mm";

            var collisionEvent = new CollisionEvent
            {
                Timestamp = DateTime.Now,
                ZoneName = zone.Name,
                Axis1Name = zone.Axis1Name,
                Axis2Name = zone.Axis2Name,
                Axis1Position = pos1,
                Axis2Position = pos2,
                Distance = distance,
                Severity = severity,
                Message = message
            };

            // 记录到事件日志
            AddEventToLog(collisionEvent);

            return collisionEvent;
        }

        /// <summary>
        /// 对所有已启用的碰撞区域执行一次全面检测。
        /// </summary>
        /// <returns>检测到的碰撞事件列表（可能为空）。</returns>
        public List<CollisionEvent> CheckAllZones()
        {
            var results = new List<CollisionEvent>();

            // 获取当前所有区域名称的快照，避免长时间持有锁
            List<string> zoneNames;
            lock (_lock)
            {
                zoneNames = _zones
                    .Where(z => z.IsEnabled)
                    .Select(z => z.Name)
                    .ToList();
            }

            // 逐一检测每个区域
            foreach (var name in zoneNames)
            {
                var evt = CheckCollision(name);
                if (evt != null)
                    results.Add(evt);
            }

            return results;
        }

        // ==================== 后台监控 ====================

        /// <summary>
        /// 启动后台碰撞监控线程，按设定的扫描间隔周期性检测所有碰撞区域。
        /// 若已在监控中则忽略重复调用。
        /// </summary>
        public void StartMonitoring()
        {
            if (_monitoring)
                return;

            _cts = new CancellationTokenSource();
            _monitoring = true;

            // 启动后台监控线程
            var thread = new Thread(() => MonitorLoop(_cts.Token))
            {
                IsBackground = true,
                Name = "CollisionDetector-Monitor"
            };
            thread.Start();

            Log("碰撞监控已启动，扫描间隔: " + _scanIntervalMs + "ms");
        }

        /// <summary>
        /// 停止后台碰撞监控线程。
        /// </summary>
        public void StopMonitoring()
        {
            if (!_monitoring)
                return;

            _cts?.Cancel();
            _monitoring = false;

            Log("碰撞监控已停止");
        }

        // ==================== 事件日志管理 ====================

        /// <summary>
        /// 获取碰撞事件历史记录（只读）。
        /// </summary>
        /// <returns>碰撞事件的只读列表。</returns>
        public IReadOnlyList<CollisionEvent> GetEventLog()
        {
            lock (_lock)
            {
                return _eventLog.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// 清空碰撞事件历史记录。
        /// </summary>
        public void ClearEventLog()
        {
            lock (_lock)
            {
                _eventLog.Clear();
            }

            Log("碰撞事件日志已清空");
        }

        // ==================== 资源释放 ====================

        /// <summary>
        /// 释放资源，停止后台监控线程。
        /// </summary>
        public void Dispose()
        {
            StopMonitoring();
            _cts?.Dispose();
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 后台监控循环 — 按扫描间隔周期性执行全区域碰撞检测。
        /// 检测到碰撞时触发 CollisionDetected 事件；
        /// 若严重程度为 "Critical"，则自动停止涉及的两个轴。
        /// </summary>
        /// <param name="ct">取消令牌，用于安全退出监控循环。</param>
        private void MonitorLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 执行一轮全区域扫描
                    var collisions = CheckAllZones();

                    foreach (var evt in collisions)
                    {
                        // 触发碰撞检测事件通知外部订阅者
                        CollisionDetected?.Invoke(this, evt);

                        // 严重碰撞：立即停止两个轴
                        if (evt.Severity == "Critical")
                        {
                            Log($"严重碰撞！正在紧急停止轴 {evt.Axis1Name} 和 {evt.Axis2Name}");

                            lock (_lock)
                            {
                                if (_axes.TryGetValue(evt.Axis1Name, out var a1))
                                    a1.Stop();
                                if (_axes.TryGetValue(evt.Axis2Name, out var a2))
                                    a2.Stop();
                            }
                        }
                    }

                    // 按扫描间隔等待，支持取消
                    ct.WaitHandle.WaitOne(_scanIntervalMs);
                }
                catch (OperationCanceledException)
                {
                    // 取消令牌触发，正常退出循环
                    break;
                }
                catch (Exception ex)
                {
                    // 捕获未预期异常，记录日志但不中断监控
                    Log($"监控循环异常: {ex.Message}");
                }
            }

            _monitoring = false;
        }

        /// <summary>
        /// 将碰撞事件添加到历史记录，超过最大容量时移除最早的记录。
        /// </summary>
        /// <param name="evt">要记录的碰撞事件。</param>
        private void AddEventToLog(CollisionEvent evt)
        {
            lock (_lock)
            {
                _eventLog.Add(evt);

                // 超过最大容量时移除最早的记录
                while (_eventLog.Count > MaxEventLogSize)
                    _eventLog.RemoveAt(0);
            }
        }

        /// <summary>
        /// 记录日志消息，并触发 MessageLogged 事件。
        /// </summary>
        /// <param name="msg">日志消息内容。</param>
        private void Log(string msg)
        {
            string formatted = $"[碰撞检测器] {DateTime.Now:HH:mm:ss.fff} {msg}";
            MessageLogged?.Invoke(this, formatted);
        }
    }
}
