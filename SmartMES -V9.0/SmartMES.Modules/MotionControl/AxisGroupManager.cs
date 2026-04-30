// ============================================================
// 文件：AxisGroupManager.cs
// 用途：30轴工业运动控制系统 — 轴组管理器
// 设计思路：
//   在多轴运动控制系统中，单独操控每一根轴效率低且容易出错。
//   本类将物理轴按照运动学关系组织为逻辑轴组（如龙门 XYZ、
//   机器人关节 J1-J6 等），提供统一的组级操作接口。
//
//   核心职责：
//   1. 轴注册与索引 — 维护全局轴字典，供组创建时引用。
//   2. 轴组生命周期 — 创建、查询、删除轴组定义。
//   3. 组级状态查询 — 批量读取组内轴的位置、状态。
//   4. 龙门双驱同步 — 管理主从轴配置，监控同步偏差。
//   5. 组级紧急停止与复位 — 对组内所有轴执行安全操作。
//
//   设计原则：
//   - 所有公开方法均做参数校验，抛出明确异常。
//   - 日志通过 MessageLogged 事件上报，不依赖具体日志框架。
//   - 线程安全：当前为单线程调用模型，后续可加锁扩展。
// ============================================================

using SmartMES.Core.Models;
using SmartMES.Core.Interfaces;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 轴组管理器 — 管理 30 轴系统中的轴组定义、状态查询、龙门同步和组级控制。
    /// </summary>
    public class AxisGroupManager
    {
        // ======================== 私有字段 ========================

        /// <summary>已注册的轴组定义字典，键为轴组名称。</summary>
        private readonly Dictionary<string, AxisGroupDefinition> _groups = new();

        /// <summary>已注册的轴控制器字典，键为轴名称。</summary>
        private readonly Dictionary<string, AxisController> _axes = new();

        /// <summary>龙门双驱同步配置字典，键为主轴名称。</summary>
        private readonly Dictionary<string, GantryConfig> _gantryConfigs = new();

        // ======================== 事件 ========================

        /// <summary>日志消息事件，用于向上层报告操作信息。</summary>
        public event EventHandler<string>? MessageLogged;

        // ======================== 轴注册 ========================

        /// <summary>
        /// 注册单个轴控制器到管理器。
        /// 注册后该轴可被轴组引用。
        /// </summary>
        /// <param name="axis">要注册的轴控制器实例。</param>
        /// <exception cref="ArgumentNullException">轴控制器为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">轴名称为空或已注册时抛出。</exception>
        public void RegisterAxis(AxisController axis)
        {
            // 参数校验：轴实例不能为空
            if (axis == null)
                throw new ArgumentNullException(nameof(axis), "轴控制器不能为 null");

            // 参数校验：轴名称不能为空
            if (string.IsNullOrWhiteSpace(axis.AxisName))
                throw new ArgumentException("轴名称不能为空", nameof(axis));

            // 参数校验：不允许重复注册同名轴
            if (_axes.ContainsKey(axis.AxisName))
                throw new ArgumentException($"轴 '{axis.AxisName}' 已经注册", nameof(axis));

            // 将轴添加到字典
            _axes[axis.AxisName] = axis;
            Log($"轴 '{axis.AxisName}' 已注册到轴组管理器");
        }

        // ======================== 轴组管理 ========================

        /// <summary>
        /// 创建一个新的轴组。
        /// 创建前会验证组名唯一性以及所有引用的轴是否已注册。
        /// </summary>
        /// <param name="definition">轴组定义，包含组名、类型和轴列表。</param>
        /// <exception cref="ArgumentNullException">定义为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">组名为空、组名重复或引用了未注册的轴时抛出。</exception>
        public void CreateGroup(AxisGroupDefinition definition)
        {
            // 参数校验：定义不能为空
            if (definition == null)
                throw new ArgumentNullException(nameof(definition), "轴组定义不能为 null");

            // 参数校验：组名不能为空
            if (string.IsNullOrWhiteSpace(definition.Name))
                throw new ArgumentException("轴组名称不能为空", nameof(definition));

            // 参数校验：不允许重复创建同名轴组
            if (_groups.ContainsKey(definition.Name))
                throw new ArgumentException($"轴组 '{definition.Name}' 已存在", nameof(definition));

            // 参数校验：轴列表不能为空
            if (definition.AxisNames == null || definition.AxisNames.Count == 0)
                throw new ArgumentException("轴组必须包含至少一个轴", nameof(definition));

            // 验证所有引用的轴名称是否已注册
            foreach (var axisName in definition.AxisNames)
            {
                if (!_axes.ContainsKey(axisName))
                    throw new ArgumentException(
                        $"轴 '{axisName}' 未注册，无法加入轴组 '{definition.Name}'",
                        nameof(definition));
            }

            // 注册轴组
            _groups[definition.Name] = definition;
            Log($"轴组 '{definition.Name}' 已创建，类型={definition.GroupType}，包含 {definition.AxisNames.Count} 个轴: [{string.Join(", ", definition.AxisNames)}]");
        }

        /// <summary>
        /// 移除指定名称的轴组。
        /// 仅移除轴组定义，不影响轴本身的注册和状态。
        /// </summary>
        /// <param name="groupName">要移除的轴组名称。</param>
        /// <exception cref="ArgumentException">组名为空或轴组不存在时抛出。</exception>
        public void RemoveGroup(string groupName)
        {
            // 参数校验
            ValidateGroupName(groupName);

            // 从字典中移除
            _groups.Remove(groupName);
            Log($"轴组 '{groupName}' 已移除");
        }

        /// <summary>
        /// 获取指定名称的轴组定义。
        /// </summary>
        /// <param name="groupName">轴组名称。</param>
        /// <returns>轴组定义，如果不存在则返回 null。</returns>
        public AxisGroupDefinition? GetGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return null;

            _groups.TryGetValue(groupName, out var group);
            return group;
        }

        /// <summary>
        /// 获取所有已注册的轴组定义列表。
        /// </summary>
        /// <returns>只读的轴组定义列表。</returns>
        public IReadOnlyList<AxisGroupDefinition> GetAllGroups()
        {
            return _groups.Values.ToList().AsReadOnly();
        }

        /// <summary>
        /// 获取指定轴组中包含的所有轴名称。
        /// </summary>
        /// <param name="groupName">轴组名称。</param>
        /// <returns>该组内的轴名称列表。</returns>
        /// <exception cref="ArgumentException">组名为空或轴组不存在时抛出。</exception>
        public List<string> GetGroupAxes(string groupName)
        {
            // 参数校验并获取轴组定义
            var group = GetValidatedGroup(groupName);
            return new List<string>(group.AxisNames);
        }

        // ======================== 轴组状态查询 ========================

        /// <summary>
        /// 检查指定轴组中的所有轴是否都处于空闲（Idle）状态。
        /// 用于判断该组是否可以接受新的运动指令。
        /// </summary>
        /// <param name="groupName">轴组名称。</param>
        /// <returns>所有轴均为 Idle 时返回 true，否则返回 false。</returns>
        /// <exception cref="ArgumentException">组名为空或轴组不存在时抛出。</exception>
        public bool AreAllAxesIdle(string groupName)
        {
            var group = GetValidatedGroup(groupName);

            // 遍历组内所有轴，检查状态是否都是 Idle
            foreach (var axisName in group.AxisNames)
            {
                if (_axes.TryGetValue(axisName, out var axis))
                {
                    if (axis.State != AxisState.Idle)
                        return false;
                }
                else
                {
                    // 轴已从注册表中消失（异常情况），视为非空闲
                    Log($"警告：轴组 '{groupName}' 中的轴 '{axisName}' 未找到");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 检查指定轴组中的所有轴是否已完成回零。
        /// 判断条件：轴状态为 Idle 且位置为 0（模拟回零完成状态）。
        /// </summary>
        /// <param name="groupName">轴组名称。</param>
        /// <returns>所有轴均已回零时返回 true，否则返回 false。</returns>
        /// <exception cref="ArgumentException">组名为空或轴组不存在时抛出。</exception>
        public bool AreAllAxesHomed(string groupName)
        {
            var group = GetValidatedGroup(groupName);

            // 回零完成的判断标准：状态为 Idle 且位置在零点附近（容差 0.01mm）
            const double homeTolerance = 0.01;

            foreach (var axisName in group.AxisNames)
            {
                if (_axes.TryGetValue(axisName, out var axis))
                {
                    // 检查状态和位置两个条件
                    if (axis.State != AxisState.Idle || Math.Abs(axis.Position) > homeTolerance)
                        return false;
                }
                else
                {
                    Log($"警告：轴组 '{groupName}' 中的轴 '{axisName}' 未找到");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 获取指定轴组中所有轴的当前位置。
        /// </summary>
        /// <param name="groupName">轴组名称。</param>
        /// <returns>字典，键为轴名称，值为当前位置（mm 或 度）。</returns>
        /// <exception cref="ArgumentException">组名为空或轴组不存在时抛出。</exception>
        public Dictionary<string, double> GetGroupPositions(string groupName)
        {
            var group = GetValidatedGroup(groupName);
            var positions = new Dictionary<string, double>();

            // 逐个读取组内轴的位置
            foreach (var axisName in group.AxisNames)
            {
                if (_axes.TryGetValue(axisName, out var axis))
                {
                    positions[axisName] = axis.Position;
                }
                else
                {
                    // 轴不存在时记录位置为 NaN，表示无效
                    positions[axisName] = double.NaN;
                    Log($"警告：轴组 '{groupName}' 中的轴 '{axisName}' 未找到，位置记为 NaN");
                }
            }

            return positions;
        }

        // ======================== 龙门双驱同步 ========================

        /// <summary>
        /// 配置龙门双驱同步参数。
        /// 龙门系统中主轴和从轴物理平行安装，需要严格同步运动，
        /// 否则会导致机械变形甚至损坏。
        /// </summary>
        /// <param name="config">龙门同步配置。</param>
        /// <exception cref="ArgumentNullException">配置为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">主轴或从轴名称为空、未注册时抛出。</exception>
        public void ConfigureGantry(GantryConfig config)
        {
            // 参数校验：配置不能为空
            if (config == null)
                throw new ArgumentNullException(nameof(config), "龙门配置不能为 null");

            // 参数校验：主轴名称有效
            if (string.IsNullOrWhiteSpace(config.MasterAxisName))
                throw new ArgumentException("主轴名称不能为空", nameof(config));

            // 参数校验：从轴名称有效
            if (string.IsNullOrWhiteSpace(config.SlaveAxisName))
                throw new ArgumentException("从轴名称不能为空", nameof(config));

            // 参数校验：主轴已注册
            if (!_axes.ContainsKey(config.MasterAxisName))
                throw new ArgumentException($"主轴 '{config.MasterAxisName}' 未注册", nameof(config));

            // 参数校验：从轴已注册
            if (!_axes.ContainsKey(config.SlaveAxisName))
                throw new ArgumentException($"从轴 '{config.SlaveAxisName}' 未注册", nameof(config));

            // 参数校验：主从轴不能是同一个轴
            if (config.MasterAxisName == config.SlaveAxisName)
                throw new ArgumentException("主轴和从轴不能是同一个轴", nameof(config));

            // 存储龙门配置，以主轴名称为键
            _gantryConfigs[config.MasterAxisName] = config;
            Log($"龙门同步已配置：主轴='{config.MasterAxisName}'，从轴='{config.SlaveAxisName}'，最大偏差={config.MaxDeviation:F3}mm，补偿增益={config.CompensationGain:F2}");
        }

        /// <summary>
        /// 检查龙门主从轴之间的位置偏差。
        /// 返回主轴与从轴位置之差的绝对值。
        /// 如果偏差超过配置的最大允许偏差，将记录警告日志。
        /// </summary>
        /// <param name="gantryMaster">龙门主轴名称。</param>
        /// <returns>主从轴位置偏差的绝对值（mm）。</returns>
        /// <exception cref="ArgumentException">主轴名称为空或未配置龙门同步时抛出。</exception>
        public double CheckGantryDeviation(string gantryMaster)
        {
            // 参数校验
            if (string.IsNullOrWhiteSpace(gantryMaster))
                throw new ArgumentException("主轴名称不能为空", nameof(gantryMaster));

            // 检查龙门配置是否存在
            if (!_gantryConfigs.TryGetValue(gantryMaster, out var config))
                throw new ArgumentException($"主轴 '{gantryMaster}' 未配置龙门同步", nameof(gantryMaster));

            // 获取主轴和从轴的当前位置
            var masterPos = _axes[config.MasterAxisName].Position;
            var slavePos = _axes[config.SlaveAxisName].Position;

            // 计算偏差绝对值
            double deviation = Math.Abs(masterPos - slavePos);

            // 如果偏差超过阈值，记录警告
            if (deviation > config.MaxDeviation)
            {
                Log($"警告：龙门同步偏差超限！主轴='{config.MasterAxisName}' 位置={masterPos:F4}mm，" +
                    $"从轴='{config.SlaveAxisName}' 位置={slavePos:F4}mm，" +
                    $"偏差={deviation:F4}mm，阈值={config.MaxDeviation:F4}mm");
            }

            return deviation;
        }

        // ======================== 组级控制操作 ========================

        /// <summary>
        /// 对指定轴组执行紧急停止。
        /// 立即停止组内所有轴的运动，不等待减速过程。
        /// </summary>
        /// <param name="groupName">要紧急停止的轴组名称。</param>
        /// <exception cref="ArgumentException">组名为空或轴组不存在时抛出。</exception>
        public void EmergencyStopGroup(string groupName)
        {
            var group = GetValidatedGroup(groupName);

            Log($"轴组 '{groupName}' 紧急停止开始...");

            // 遍历组内所有轴，逐一执行停止
            int stoppedCount = 0;
            foreach (var axisName in group.AxisNames)
            {
                if (_axes.TryGetValue(axisName, out var axis))
                {
                    axis.Stop();
                    stoppedCount++;
                }
                else
                {
                    Log($"警告：轴组 '{groupName}' 中的轴 '{axisName}' 未找到，跳过停止");
                }
            }

            Log($"轴组 '{groupName}' 紧急停止完成，已停止 {stoppedCount}/{group.AxisNames.Count} 个轴");
        }

        /// <summary>
        /// 对指定轴组执行复位操作。
        /// 将组内所有处于错误状态的轴复位回空闲状态。
        /// </summary>
        /// <param name="groupName">要复位的轴组名称。</param>
        /// <exception cref="ArgumentException">组名为空或轴组不存在时抛出。</exception>
        public void ResetGroup(string groupName)
        {
            var group = GetValidatedGroup(groupName);

            Log($"轴组 '{groupName}' 复位开始...");

            // 遍历组内所有轴，逐一执行复位
            int resetCount = 0;
            foreach (var axisName in group.AxisNames)
            {
                if (_axes.TryGetValue(axisName, out var axis))
                {
                    // Reset() 仅在 Error 状态下有效，其他状态会返回 false
                    if (axis.Reset())
                        resetCount++;
                }
                else
                {
                    Log($"警告：轴组 '{groupName}' 中的轴 '{axisName}' 未找到，跳过复位");
                }
            }

            Log($"轴组 '{groupName}' 复位完成，成功复位 {resetCount}/{group.AxisNames.Count} 个轴");
        }

        // ======================== 私有辅助方法 ========================

        /// <summary>
        /// 记录日志消息，通过 MessageLogged 事件发布。
        /// </summary>
        /// <param name="msg">日志消息内容。</param>
        private void Log(string msg)
        {
            MessageLogged?.Invoke(this, $"[AxisGroupManager] {msg}");
        }

        /// <summary>
        /// 验证轴组名称非空且对应的轴组存在。
        /// </summary>
        /// <param name="groupName">要验证的轴组名称。</param>
        /// <exception cref="ArgumentException">组名为空或轴组不存在时抛出。</exception>
        private void ValidateGroupName(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("轴组名称不能为空", nameof(groupName));

            if (!_groups.ContainsKey(groupName))
                throw new ArgumentException($"轴组 '{groupName}' 不存在", nameof(groupName));
        }

        /// <summary>
        /// 验证轴组名称并返回对应的轴组定义。
        /// 合并了校验与查询两步操作，减少重复代码。
        /// </summary>
        /// <param name="groupName">轴组名称。</param>
        /// <returns>验证通过后的轴组定义。</returns>
        /// <exception cref="ArgumentException">组名为空或轴组不存在时抛出。</exception>
        private AxisGroupDefinition GetValidatedGroup(string groupName)
        {
            ValidateGroupName(groupName);
            return _groups[groupName];
        }
    }
}
