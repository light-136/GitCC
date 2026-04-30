// ============================================================
// 文件：ThirtyAxisController.cs
// 用途：30轴工业级运动控制器 — 多通道/多轴组的顶层编排
// 设计思路：
//   工业级半导体设备通常需要管理大量运动轴（20~50轴），
//   涉及多龙门、多工位、多通道并行加工等场景。
//
//   本控制器将 30 个运动轴组织为 6 个轴组：
//     ① 主龙门组（Gantry1）：X1, Y1, Z1, A1, B1 — 5轴
//     ② 副龙门组（Gantry2）：X2, Y2, Z2, A2, B2 — 5轴
//     ③ 机械臂组（Robot）  ：J1~J6            — 6轴
//     ④ 传送带组（Conveyor）：C1~C4            — 4轴
//     ⑤ 主轴组（Spindle）  ：S1~S4            — 4轴
//     ⑥ 辅助轴组（Aux）    ：AUX1~AUX6        — 6轴
//                                        合计 30轴
//
//   功能整合：
//     - AxisGroupManager   — 轴组创建/管理/龙门同步
//     - MultiChannelController — 多通道并行 G 代码执行
//     - CollisionDetector  — 碰撞区域检测与安全互锁
//     - PerformanceMonitor — 运动性能采样与报告
//     - HomingService      — 多策略回零
//     - CoordinateManager  — 坐标系变换
//
//   典型工作流程：
//     1. 初始化 → 所有轴创建并注册到各子模块
//     2. 回零    → 分组并行回零
//     3. 加载程序 → 多通道各自加载 G 代码
//     4. 运行    → 多通道并行执行，碰撞监控同步运行
//     5. 停止    → 程序结束或急停 → 性能报告生成
// ============================================================

using SmartMES.Core.Models;
using SmartMES.Core.Interfaces;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 30轴工业级运动控制器 — 集成轴组管理、多通道执行、
    /// 碰撞检测、性能监控的完整运动控制系统。
    /// </summary>
    public class ThirtyAxisController : IDisposable
    {
        // ======================== 默认30轴配置 ========================

        /// <summary>
        /// 默认30轴配置表 — 6组共30轴。
        /// 每项：(轴名, 轴类型, 最大速度, 最大加速度, 所属组名, 通道号)
        /// </summary>
        private static readonly AxisConfig[] DefaultAxisConfigs =
        {
            // 主龙门组 — 高速高精度XYZ定位 + 2个旋转轴
            new() { Name = "X1", AxisType = AxisType.Linear,  MaxVelocity = 800, MaxAcceleration = 4000, GroupName = "Gantry1", ChannelId = 0,
                    SoftLimitPositive = 500, SoftLimitNegative = -500 },
            new() { Name = "Y1", AxisType = AxisType.Linear,  MaxVelocity = 800, MaxAcceleration = 4000, GroupName = "Gantry1", ChannelId = 0,
                    SoftLimitPositive = 400, SoftLimitNegative = -400 },
            new() { Name = "Z1", AxisType = AxisType.Linear,  MaxVelocity = 300, MaxAcceleration = 2000, GroupName = "Gantry1", ChannelId = 0,
                    SoftLimitPositive = 200, SoftLimitNegative = 0 },
            new() { Name = "A1", AxisType = AxisType.Rotary,  MaxVelocity = 360, MaxAcceleration = 1800, GroupName = "Gantry1", ChannelId = 0 },
            new() { Name = "B1", AxisType = AxisType.Rotary,  MaxVelocity = 360, MaxAcceleration = 1800, GroupName = "Gantry1", ChannelId = 0 },

            // 副龙门组 — 与主龙门共享工作空间，需碰撞检测
            new() { Name = "X2", AxisType = AxisType.Linear,  MaxVelocity = 600, MaxAcceleration = 3000, GroupName = "Gantry2", ChannelId = 1,
                    SoftLimitPositive = 500, SoftLimitNegative = -500 },
            new() { Name = "Y2", AxisType = AxisType.Linear,  MaxVelocity = 600, MaxAcceleration = 3000, GroupName = "Gantry2", ChannelId = 1,
                    SoftLimitPositive = 400, SoftLimitNegative = -400 },
            new() { Name = "Z2", AxisType = AxisType.Linear,  MaxVelocity = 250, MaxAcceleration = 1500, GroupName = "Gantry2", ChannelId = 1,
                    SoftLimitPositive = 200, SoftLimitNegative = 0 },
            new() { Name = "A2", AxisType = AxisType.Rotary,  MaxVelocity = 360, MaxAcceleration = 1800, GroupName = "Gantry2", ChannelId = 1 },
            new() { Name = "B2", AxisType = AxisType.Rotary,  MaxVelocity = 360, MaxAcceleration = 1800, GroupName = "Gantry2", ChannelId = 1 },

            // 机械臂组 — 6自由度关节式
            new() { Name = "J1", AxisType = AxisType.Rotary,  MaxVelocity = 180, MaxAcceleration = 900,  GroupName = "Robot", ChannelId = 2 },
            new() { Name = "J2", AxisType = AxisType.Rotary,  MaxVelocity = 180, MaxAcceleration = 900,  GroupName = "Robot", ChannelId = 2 },
            new() { Name = "J3", AxisType = AxisType.Rotary,  MaxVelocity = 180, MaxAcceleration = 900,  GroupName = "Robot", ChannelId = 2 },
            new() { Name = "J4", AxisType = AxisType.Rotary,  MaxVelocity = 360, MaxAcceleration = 1800, GroupName = "Robot", ChannelId = 2 },
            new() { Name = "J5", AxisType = AxisType.Rotary,  MaxVelocity = 360, MaxAcceleration = 1800, GroupName = "Robot", ChannelId = 2 },
            new() { Name = "J6", AxisType = AxisType.Rotary,  MaxVelocity = 720, MaxAcceleration = 3600, GroupName = "Robot", ChannelId = 2 },

            // 传送带组 — 物料搬运
            new() { Name = "C1", AxisType = AxisType.Linear,  MaxVelocity = 500, MaxAcceleration = 2000, GroupName = "Conveyor", ChannelId = 3 },
            new() { Name = "C2", AxisType = AxisType.Linear,  MaxVelocity = 500, MaxAcceleration = 2000, GroupName = "Conveyor", ChannelId = 3 },
            new() { Name = "C3", AxisType = AxisType.Linear,  MaxVelocity = 300, MaxAcceleration = 1500, GroupName = "Conveyor", ChannelId = 3 },
            new() { Name = "C4", AxisType = AxisType.Linear,  MaxVelocity = 300, MaxAcceleration = 1500, GroupName = "Conveyor", ChannelId = 3 },

            // 主轴组 — 高速旋转轴
            new() { Name = "S1", AxisType = AxisType.Spindle, MaxVelocity = 3000, MaxAcceleration = 10000, GroupName = "Spindle", ChannelId = 4 },
            new() { Name = "S2", AxisType = AxisType.Spindle, MaxVelocity = 3000, MaxAcceleration = 10000, GroupName = "Spindle", ChannelId = 4 },
            new() { Name = "S3", AxisType = AxisType.Spindle, MaxVelocity = 1500, MaxAcceleration = 5000,  GroupName = "Spindle", ChannelId = 4 },
            new() { Name = "S4", AxisType = AxisType.Spindle, MaxVelocity = 1500, MaxAcceleration = 5000,  GroupName = "Spindle", ChannelId = 4 },

            // 辅助轴组 — 定位/夹持/辅助功能
            new() { Name = "AUX1", AxisType = AxisType.Linear, MaxVelocity = 200, MaxAcceleration = 1000, GroupName = "Auxiliary", ChannelId = 5 },
            new() { Name = "AUX2", AxisType = AxisType.Linear, MaxVelocity = 200, MaxAcceleration = 1000, GroupName = "Auxiliary", ChannelId = 5 },
            new() { Name = "AUX3", AxisType = AxisType.Linear, MaxVelocity = 100, MaxAcceleration = 500,  GroupName = "Auxiliary", ChannelId = 5 },
            new() { Name = "AUX4", AxisType = AxisType.Linear, MaxVelocity = 100, MaxAcceleration = 500,  GroupName = "Auxiliary", ChannelId = 5 },
            new() { Name = "AUX5", AxisType = AxisType.Rotary, MaxVelocity = 180, MaxAcceleration = 900,  GroupName = "Auxiliary", ChannelId = 5 },
            new() { Name = "AUX6", AxisType = AxisType.Rotary, MaxVelocity = 180, MaxAcceleration = 900,  GroupName = "Auxiliary", ChannelId = 5 },
        };

        // ======================== 子模块实例 ========================

        /// <summary>底层多轴控制器（持有所有 AxisController 实例）。</summary>
        private readonly MultiAxisController _multiAxis = new();

        /// <summary>轴组管理器。</summary>
        public AxisGroupManager GroupManager { get; }

        /// <summary>多通道控制器。</summary>
        public MultiChannelController ChannelController { get; }

        /// <summary>碰撞检测器。</summary>
        public CollisionDetector CollisionDetector { get; }

        /// <summary>性能监控器。</summary>
        public PerformanceMonitor PerformanceMonitor { get; }

        /// <summary>回零服务。</summary>
        public HomingService HomingService { get; }

        /// <summary>坐标系管理器。</summary>
        public CoordinateManager CoordinateManager { get; }

        /// <summary>所有轴配置信息（只读）。</summary>
        public IReadOnlyList<AxisConfig> AxisConfigs { get; }

        /// <summary>所有轴控制器引用。</summary>
        public IReadOnlyDictionary<string, AxisController> Axes => _multiAxis.Axes;

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        // ======================== 构造与初始化 ========================

        /// <summary>
        /// 创建 30 轴控制器（使用默认配置）。
        /// </summary>
        public ThirtyAxisController() : this(DefaultAxisConfigs) { }

        /// <summary>
        /// 创建 30 轴控制器（自定义轴配置）。
        /// </summary>
        /// <param name="configs">轴配置数组。</param>
        public ThirtyAxisController(AxisConfig[] configs)
        {
            AxisConfigs = configs;

            // 创建子模块
            GroupManager = new AxisGroupManager();
            ChannelController = new MultiChannelController();
            CollisionDetector = new CollisionDetector();
            PerformanceMonitor = new PerformanceMonitor();
            HomingService = new HomingService();
            CoordinateManager = new CoordinateManager();

            // 转发子模块日志
            GroupManager.MessageLogged += (_, msg) => Log($"[轴组] {msg}");
            ChannelController.MessageLogged += (_, msg) => Log($"[通道] {msg}");
            CollisionDetector.MessageLogged += (_, msg) => Log($"[碰撞] {msg}");
            PerformanceMonitor.MessageLogged += (_, msg) => Log($"[性能] {msg}");
            HomingService.MessageLogged += (_, msg) => Log($"[回零] {msg}");

            // 碰撞事件联动性能监控
            CollisionDetector.CollisionDetected += (_, evt) =>
            {
                PerformanceMonitor.IncrementCollisionWarning();
                Log($"[碰撞告警] {evt.ZoneName}: {evt.Message}");
            };

            // 初始化轴
            InitializeAxes(configs);
            Log($"[30轴控制器] 初始化完成，共 {configs.Length} 轴，{GetGroupNames().Count} 组");
        }

        /// <summary>
        /// 初始化所有轴 — 创建 AxisController 并注册到各子模块。
        /// </summary>
        private void InitializeAxes(AxisConfig[] configs)
        {
            // 按组分类
            var groupAxes = new Dictionary<string, List<string>>();

            foreach (var cfg in configs)
            {
                // 创建轴并设置参数
                _multiAxis.AddAxis(cfg.Name, cfg.MaxVelocity, cfg.MaxAcceleration);

                var axis = _multiAxis.Axes[cfg.Name];

                // 注册到各子模块
                GroupManager.RegisterAxis(axis);
                ChannelController.RegisterAxis(axis);
                CollisionDetector.RegisterAxis(axis);
                PerformanceMonitor.RegisterAxis(axis);

                // 收集分组信息
                if (!string.IsNullOrEmpty(cfg.GroupName))
                {
                    if (!groupAxes.ContainsKey(cfg.GroupName))
                        groupAxes[cfg.GroupName] = new List<string>();
                    groupAxes[cfg.GroupName].Add(cfg.Name);
                }
            }

            // 创建轴组
            foreach (var (groupName, axisNames) in groupAxes)
            {
                var groupType = InferGroupType(groupName);
                GroupManager.CreateGroup(new AxisGroupDefinition
                {
                    Name = groupName,
                    GroupType = groupType,
                    AxisNames = axisNames,
                    MaxInterpolationVelocity = configs
                        .Where(c => c.GroupName == groupName)
                        .Min(c => c.MaxVelocity)
                });
            }

            // 创建通道
            var channelAxes = configs
                .GroupBy(c => c.ChannelId)
                .OrderBy(g => g.Key);

            foreach (var ch in channelAxes)
            {
                ChannelController.AddChannel(new ChannelConfig
                {
                    Id = ch.Key,
                    Name = $"CH{ch.Key}",
                    AxisNames = ch.Select(c => c.Name).ToList(),
                    DefaultFeedRate = ch.Min(c => c.MaxVelocity) * 60
                });
            }

            // 配置默认碰撞区域 — 主龙门与副龙门的X轴互锁
            SetupDefaultCollisionZones();
        }

        /// <summary>
        /// 根据组名推断轴组类型。
        /// </summary>
        private static AxisGroupType InferGroupType(string groupName)
        {
            if (groupName.Contains("Gantry", StringComparison.OrdinalIgnoreCase))
                return AxisGroupType.Gantry;
            if (groupName.Contains("Robot", StringComparison.OrdinalIgnoreCase))
                return AxisGroupType.Custom;
            if (groupName.Contains("Spindle", StringComparison.OrdinalIgnoreCase))
                return AxisGroupType.Rotary;
            return AxisGroupType.Cartesian;
        }

        /// <summary>
        /// 配置默认碰撞区域 — 主副龙门X轴防撞。
        /// 当两个龙门的X轴同时在 [-100, 100] 范围内时报警。
        /// </summary>
        private void SetupDefaultCollisionZones()
        {
            if (_multiAxis.Axes.ContainsKey("X1") && _multiAxis.Axes.ContainsKey("X2"))
            {
                CollisionDetector.AddZone(new CollisionZone
                {
                    Name = "Gantry_X_Collision",
                    Axis1Name = "X1",
                    Axis2Name = "X2",
                    Axis1Min = -100, Axis1Max = 100,
                    Axis2Min = -100, Axis2Max = 100,
                    MinSafeDistance = 20.0,
                    IsEnabled = true
                });

                Log("[碰撞] 已配置主副龙门X轴防撞区域");
            }
        }

        // ======================== 回零操作 ========================

        /// <summary>
        /// 全部 30 轴并行回零。
        /// </summary>
        public async Task HomeAllAxesAsync(CancellationToken ct = default)
        {
            Log("[30轴控制器] 开始全轴回零...");
            var axes = _multiAxis.Axes.Values.ToList();
            await HomingService.HomeParallelAsync(axes, null, ct);
            Log("[30轴控制器] 全轴回零完成");
        }

        /// <summary>
        /// 按组回零 — 指定组内的轴并行回零。
        /// </summary>
        public async Task HomeGroupAsync(string groupName, CancellationToken ct = default)
        {
            var axisNames = GroupManager.GetGroupAxes(groupName);
            if (axisNames.Count == 0)
                throw new ArgumentException($"轴组 '{groupName}' 不存在或为空");

            Log($"[30轴控制器] 开始轴组 '{groupName}' 回零（{axisNames.Count} 轴）...");
            var axes = axisNames
                .Where(n => _multiAxis.Axes.ContainsKey(n))
                .Select(n => _multiAxis.Axes[n])
                .ToList();

            await HomingService.HomeParallelAsync(axes, null, ct);
            Log($"[30轴控制器] 轴组 '{groupName}' 回零完成");
        }

        // ======================== 运动执行 ========================

        /// <summary>
        /// 单轴绝对定位运动。
        /// </summary>
        public bool MoveAxis(string axisName, double target)
        {
            if (!_multiAxis.Axes.TryGetValue(axisName, out var axis))
                throw new ArgumentException($"轴 '{axisName}' 不存在");

            // 软限位检查
            var cfg = AxisConfigs.FirstOrDefault(c => c.Name == axisName);
            if (cfg != null)
            {
                if (target > cfg.SoftLimitPositive || target < cfg.SoftLimitNegative)
                {
                    Log($"[软限位] 轴 {axisName} 目标位置 {target:F2} 超出软限位 [{cfg.SoftLimitNegative:F1}, {cfg.SoftLimitPositive:F1}]");
                    return false;
                }
            }

            return axis.MoveTo(target);
        }

        /// <summary>
        /// 多轴协调运动 — 在指定轴组内执行多轴联动。
        /// </summary>
        public async Task CoordinatedMoveAsync(string groupName, Dictionary<string, double> targets,
            double feedRate = 100, CancellationToken ct = default)
        {
            var groupAxes = GroupManager.GetGroupAxes(groupName);
            if (groupAxes.Count == 0)
                throw new ArgumentException($"轴组 '{groupName}' 不存在");

            // 检查所有目标轴属于该组
            foreach (var axisName in targets.Keys)
            {
                if (!groupAxes.Contains(axisName))
                    throw new ArgumentException($"轴 '{axisName}' 不属于轴组 '{groupName}'");
            }

            var point = new InterpolationPoint
            {
                AxisTargets = new Dictionary<string, double>(targets),
                FeedRate = feedRate
            };

            await _multiAxis.LinearInterpolateAsync(point);

            // 等待运动完成
            while (!ct.IsCancellationRequested)
            {
                bool allIdle = targets.Keys.All(n =>
                    _multiAxis.Axes.TryGetValue(n, out var a) && a.State == AxisState.Idle);
                if (allIdle) break;
                await Task.Delay(10, ct);
            }
        }

        // ======================== 多通道执行 ========================

        /// <summary>
        /// 在指定通道上执行 G 代码程序。
        /// </summary>
        public async Task RunChannelProgramAsync(int channelId, string program,
            CancellationToken ct = default)
        {
            Log($"[30轴控制器] 通道 {channelId} 开始执行程序（{program.Split('\n').Length} 行）");
            await ChannelController.StartChannelAsync(channelId, program, ct);
            Log($"[30轴控制器] 通道 {channelId} 程序执行完成");
        }

        /// <summary>
        /// 多通道并行执行 G 代码程序。
        /// </summary>
        /// <param name="programs">通道号→G代码程序的映射。</param>
        public async Task RunMultiChannelAsync(Dictionary<int, string> programs,
            CancellationToken ct = default)
        {
            Log($"[30轴控制器] 启动 {programs.Count} 通道并行执行");

            // 启动碰撞监控
            CollisionDetector.StartMonitoring();
            PerformanceMonitor.StartRecording();

            try
            {
                await ChannelController.StartAllChannelsAsync(programs, ct);
                Log("[30轴控制器] 所有通道执行完成");
            }
            finally
            {
                CollisionDetector.StopMonitoring();
                PerformanceMonitor.StopRecording();
            }
        }

        // ======================== 安全控制 ========================

        /// <summary>
        /// 全系统急停 — 立即停止所有 30 轴。
        /// </summary>
        public void EmergencyStop()
        {
            Log("[30轴控制器] *** 急停 ***");
            ChannelController.StopAllChannels();
            _multiAxis.EmergencyStop();
            CollisionDetector.StopMonitoring();
            PerformanceMonitor.StopRecording();
        }

        /// <summary>
        /// 指定轴组急停。
        /// </summary>
        public void EmergencyStopGroup(string groupName)
        {
            Log($"[30轴控制器] 轴组 '{groupName}' 急停");
            GroupManager.EmergencyStopGroup(groupName);
        }

        /// <summary>
        /// 全系统复位。
        /// </summary>
        public void ResetAll()
        {
            Log("[30轴控制器] 全系统复位");
            _multiAxis.ResetAll();
        }

        // ======================== 查询与诊断 ========================

        /// <summary>
        /// 获取所有轴的当前位置。
        /// </summary>
        public Dictionary<string, double> GetAllPositions()
        {
            return _multiAxis.Axes
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Position);
        }

        /// <summary>
        /// 获取所有轴组名称。
        /// </summary>
        public List<string> GetGroupNames()
        {
            return GroupManager.GetAllGroups().Select(g => g.Name).ToList();
        }

        /// <summary>
        /// 获取系统性能报告。
        /// </summary>
        public SystemPerformanceReport GetPerformanceReport(double windowSeconds = 60)
        {
            return PerformanceMonitor.GenerateReport(windowSeconds);
        }

        /// <summary>
        /// 获取指定轴组的状态汇总。
        /// </summary>
        public Dictionary<string, (AxisState State, double Position)> GetGroupStatus(string groupName)
        {
            var axisNames = GroupManager.GetGroupAxes(groupName);
            var result = new Dictionary<string, (AxisState, double)>();

            foreach (var name in axisNames)
            {
                if (_multiAxis.Axes.TryGetValue(name, out var axis))
                    result[name] = (axis.State, axis.Position);
            }

            return result;
        }

        /// <summary>
        /// 获取多通道执行状态。
        /// </summary>
        public Dictionary<int, (ChannelState State, int CurrentLine, int TotalLines)>
            GetChannelStatus()
        {
            return ChannelController.GetAllChannelStatus();
        }

        // ======================== 示例程序 ========================

        /// <summary>
        /// 获取双龙门并行加工示例程序。
        /// 通道0（主龙门）和通道1（副龙门）各自独立执行。
        /// </summary>
        public static Dictionary<int, string> GetDualGantrySamplePrograms()
        {
            return new Dictionary<int, string>
            {
                [0] = @"
; === 主龙门加工程序 (通道0) ===
G90          ; 绝对坐标模式
G0 X1 50 Y1 50  ; 快速移动到起始位
G1 X1 100 Y1 50 F500  ; 直线加工
G1 X1 100 Y1 100
G1 X1 50 Y1 100
G1 X1 50 Y1 50
G0 X1 0 Y1 0    ; 返回原点
M30",
                [1] = @"
; === 副龙门加工程序 (通道1) ===
G90
G0 X2 -50 Y2 -50  ; 快速移动到起始位（远离主龙门）
G1 X2 -100 Y2 -50 F400
G1 X2 -100 Y2 -100
G1 X2 -50 Y2 -100
G1 X2 -50 Y2 -50
G0 X2 0 Y2 0
M30"
            };
        }

        // ======================== 资源释放 ========================

        /// <summary>
        /// 释放所有子模块资源。
        /// </summary>
        public void Dispose()
        {
            CollisionDetector.Dispose();
            PerformanceMonitor.Dispose();
            Log("[30轴控制器] 已释放所有资源");
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
