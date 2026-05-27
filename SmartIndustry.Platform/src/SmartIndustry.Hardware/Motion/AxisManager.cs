// ============================================================
// 文件：AxisManager.cs
// 层级：硬件抽象层（Hardware Layer）> Motion
// 职责：多轴管理器，统一管理多张运动控制卡上的所有轴控制器。
//       提供全局急停、全局使能/去使能、轴组管理等高层操作。
//
// 设计思路：
//   AxisManager 是运动控制子系统的门面（Facade），
//   上层代码（Application 层）通过 AxisManager 访问所有轴，
//   无需关心轴属于哪张卡、使用哪个索引。
//
//   轴组（AxisGroup）：将相关轴组合在一起统一操作，
//   例如 XY 工作台组、Z轴组、旋转组。
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Models;
using SmartIndustry.Domain.Enums;
using SmartIndustry.Hardware.Motion.Drivers;
using SmartIndustry.Hardware.Motion.Scheduler;

namespace SmartIndustry.Hardware.Motion
{
    /// <summary>
    /// 轴组定义 — 将多个轴逻辑分组，可对组内所有轴统一操作。
    /// </summary>
    public class AxisGroup
    {
        /// <summary>组名称（如"XY_Stage"、"Z_Axis"）</summary>
        public string GroupName { get; set; } = string.Empty;

        /// <summary>组内轴ID列表</summary>
        public List<string> AxisIds { get; set; } = new();

        /// <summary>组描述</summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 轴状态汇总 — 包含所有轴的状态快照，用于监控和诊断。
    /// </summary>
    public class AxisManagerSummary
    {
        /// <summary>总轴数</summary>
        public int TotalAxisCount { get; set; }

        /// <summary>使能中的轴数</summary>
        public int EnabledAxisCount { get; set; }

        /// <summary>运动中的轴数</summary>
        public int MovingAxisCount { get; set; }

        /// <summary>错误轴数</summary>
        public int ErrorAxisCount { get; set; }

        /// <summary>是否处于急停状态</summary>
        public bool IsEmergencyStop { get; set; }

        /// <summary>各轴状态详情</summary>
        public Dictionary<string, AxisStatus> AxisStatuses { get; set; } = new();

        /// <summary>汇总时间戳</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 多轴管理器。
    /// 管理所有运动控制卡和轴控制器，提供：
    ///   - 轴注册（按ID注册 AxisController）
    ///   - 全局急停（一键停止所有轴）
    ///   - 全局使能/去使能
    ///   - 按组操作（组内轴并发使能/停止）
    ///   - 轴状态汇总查询
    ///   - 与 MotionScheduler 协同（共享轴映射）
    ///
    /// 使用方式：
    ///   var manager = new AxisManager(eventBus);
    ///   manager.RegisterAxis("X", xAxisController);
    ///   manager.RegisterAxis("Y", yAxisController);
    ///   manager.DefineGroup("XY", new[]{"X","Y"});
    ///   await manager.EnableAllAsync();
    ///   var scheduler = manager.CreateScheduler();
    /// </summary>
    public class AxisManager : IDisposable
    {
        // ==================== 私有字段 ====================

        /// <summary>轴控制器注册表（Key=AxisId）</summary>
        private readonly Dictionary<string, AxisController> _axes = new();

        /// <summary>轴组注册表（Key=GroupName）</summary>
        private readonly Dictionary<string, AxisGroup> _groups = new();

        /// <summary>事件总线</summary>
        private readonly IEventBus _eventBus;

        /// <summary>关联的调度器（可选，由 CreateScheduler 创建）</summary>
        private MotionScheduler? _scheduler;

        /// <summary>是否处于急停状态</summary>
        private volatile bool _isEmergencyStop;

        /// <summary>访问轴字典的锁</summary>
        private readonly object _axesLock = new();

        // ==================== 事件 ====================

        /// <summary>发生全局急停时触发</summary>
        public event EventHandler? EmergencyStopTriggered;

        /// <summary>急停解除时触发</summary>
        public event EventHandler? EmergencyStopReleased;

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造多轴管理器
        /// </summary>
        /// <param name="eventBus">事件总线（用于发布轴状态变化事件）</param>
        public AxisManager(IEventBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        // ==================== 轴注册 ====================

        /// <summary>
        /// 注册一个轴控制器。
        /// 同一 AxisId 不允许重复注册。
        /// </summary>
        /// <param name="axisId">轴唯一标识</param>
        /// <param name="controller">轴控制器实例</param>
        public void RegisterAxis(string axisId, AxisController controller)
        {
            lock (_axesLock)
            {
                if (_axes.ContainsKey(axisId))
                    throw new InvalidOperationException($"轴[{axisId}]已注册，不允许重复注册");
                _axes[axisId] = controller;
            }
        }

        /// <summary>
        /// 按轴ID获取轴控制器（不存在返回 null）
        /// </summary>
        public AxisController? GetAxis(string axisId)
        {
            lock (_axesLock)
            {
                _axes.TryGetValue(axisId, out var controller);
                return controller;
            }
        }

        /// <summary>
        /// 获取已注册的所有轴ID
        /// </summary>
        public IReadOnlyList<string> AxisIds
        {
            get { lock (_axesLock) return _axes.Keys.ToList(); }
        }

        // ==================== 轴组管理 ====================

        /// <summary>
        /// 定义一个轴组
        /// </summary>
        /// <param name="groupName">组名</param>
        /// <param name="axisIds">组内轴ID数组</param>
        /// <param name="description">组描述</param>
        public void DefineGroup(string groupName, string[] axisIds, string description = "")
        {
            lock (_axesLock)
            {
                // 验证所有轴ID均已注册
                foreach (var id in axisIds)
                {
                    if (!_axes.ContainsKey(id))
                        throw new ArgumentException($"轴组[{groupName}]中的轴[{id}]尚未注册");
                }
                _groups[groupName] = new AxisGroup
                {
                    GroupName = groupName,
                    AxisIds = axisIds.ToList(),
                    Description = description
                };
            }
        }

        /// <summary>
        /// 获取组内所有轴控制器
        /// </summary>
        public IReadOnlyList<AxisController> GetGroupAxes(string groupName)
        {
            lock (_axesLock)
            {
                if (!_groups.TryGetValue(groupName, out var group))
                    throw new KeyNotFoundException($"轴组[{groupName}]不存在");
                return group.AxisIds.Select(id => _axes[id]).ToList();
            }
        }

        // ==================== 全局操作 ====================

        /// <summary>
        /// 使能所有已注册的轴（并发执行，等待全部完成）
        /// </summary>
        public async Task EnableAllAsync()
        {
            if (_isEmergencyStop) throw new InvalidOperationException("急停状态下无法使能轴");
            var tasks = new List<Task>();
            lock (_axesLock)
            {
                foreach (var axis in _axes.Values)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        try { axis.Enable(); }
                        catch { /* 忽略单轴使能失败，继续使能其他轴 */ }
                    }));
                }
            }
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 去使能所有轴（并发执行）
        /// </summary>
        public async Task DisableAllAsync()
        {
            var tasks = new List<Task>();
            lock (_axesLock)
            {
                foreach (var axis in _axes.Values)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        try { axis.Disable(); }
                        catch { }
                    }));
                }
            }
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 全局急停 — 立即停止所有轴，设置急停标志。
        /// 急停后需调用 ReleaseEmergencyStop() 才能恢复使用。
        /// </summary>
        public void EmergencyStopAll()
        {
            _isEmergencyStop = true;

            // 停止调度器（不再执行新任务）
            _scheduler?.Pause();

            // 并发停止所有轴
            lock (_axesLock)
            {
                foreach (var axis in _axes.Values)
                {
                    try { axis.EmergencyStop(); }
                    catch { /* 急停不允许失败，即使单轴异常也继续 */ }
                }
            }

            EmergencyStopTriggered?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 解除急停（需清除所有轴错误后才能操作）
        /// </summary>
        public void ReleaseEmergencyStop()
        {
            lock (_axesLock)
            {
                // 清除所有轴错误
                foreach (var axis in _axes.Values)
                {
                    try { axis.ClearError(); }
                    catch { }
                }
            }
            _isEmergencyStop = false;
            _scheduler?.Resume();
            EmergencyStopReleased?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 停止组内所有轴（减速停止）
        /// </summary>
        public void StopGroup(string groupName)
        {
            foreach (var axis in GetGroupAxes(groupName))
            {
                try { axis.Stop(); }
                catch { }
            }
        }

        // ==================== 状态查询 ====================

        /// <summary>
        /// 获取所有轴的状态汇总
        /// </summary>
        public AxisManagerSummary GetSummary()
        {
            var summary = new AxisManagerSummary
            {
                IsEmergencyStop = _isEmergencyStop
            };

            lock (_axesLock)
            {
                summary.TotalAxisCount = _axes.Count;
                foreach (var (id, axis) in _axes)
                {
                    var status = axis.GetStatus();
                    summary.AxisStatuses[id] = status;
                    if (status.IsEnabled) summary.EnabledAxisCount++;
                    if (status.IsMoving) summary.MovingAxisCount++;
                    if (status.HasError) summary.ErrorAxisCount++;
                }
            }
            return summary;
        }

        /// <summary>
        /// 检查所有轴是否处于静止状态（无运动中的轴）
        /// </summary>
        public bool IsAllIdle()
        {
            lock (_axesLock)
            {
                return _axes.Values.All(a => a.CurrentState != AxisState.Moving);
            }
        }

        // ==================== 与调度器协同 ====================

        /// <summary>
        /// 创建并关联运动任务调度器（使用当前所有轴的只读视图）
        /// </summary>
        public MotionScheduler CreateScheduler()
        {
            Dictionary<string, AxisController> axisMap;
            lock (_axesLock) { axisMap = new Dictionary<string, AxisController>(_axes); }

            _scheduler = new MotionScheduler(axisMap);
            _scheduler.Start();
            return _scheduler;
        }

        // ==================== IDisposable ====================

        /// <inheritdoc/>
        public void Dispose()
        {
            _scheduler?.Dispose();
            lock (_axesLock)
            {
                foreach (var axis in _axes.Values)
                    axis.Dispose();
                _axes.Clear();
            }
        }
    }
}
