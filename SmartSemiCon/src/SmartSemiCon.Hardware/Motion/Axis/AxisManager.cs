// ============================================================
// 文件：AxisManager.cs
// 用途：轴管理器 — 管理所有运动轴的核心服务
// 设计思路：
//   AxisManager是运动控制的入口点，管理所有控制卡和轴。
//   上层代码通过轴ID访问轴，不需要知道轴在哪张卡上。
//
//   职责：
//   1. 管理多张控制卡（支持不同品牌混合使用）
//   2. 提供统一的轴访问接口（按轴ID查找）
//   3. 管理所有轴的初始化和关闭
//   4. 提供全局急停功能
// ============================================================

using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;
using SmartSemiCon.Hardware.Motion.Drivers;

namespace SmartSemiCon.Hardware.Motion.Axis
{
    /// <summary>
    /// 轴管理器 — 运动控制系统的核心服务。
    /// 管理所有控制卡和轴，提供统一的访问接口。
    /// </summary>
    public class AxisManager : IDisposable
    {
        // 控制卡列表
        private readonly Dictionary<int, IMotionCard> _cards = new();

        // 轴ID → 控制器的快速索引
        private readonly Dictionary<int, IAxisController> _axes = new();

        // 轴ID → 配置
        private readonly Dictionary<int, AxisConfig> _configs = new();

        private readonly IEventBus _eventBus;
        private bool _isInitialized;

        /// <summary>所有已配置的轴数量</summary>
        public int AxisCount => _axes.Count;

        /// <summary>已配置的轴ID列表</summary>
        public IReadOnlyList<int> AxisIds => _axes.Keys.ToList().AsReadOnly();

        /// <summary>是否已初始化</summary>
        public bool IsInitialized => _isInitialized;

        public AxisManager(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        /// <summary>
        /// 批量配置轴 — 根据配置列表创建控制卡和轴。
        /// </summary>
        /// <param name="configs">轴配置列表</param>
        public void Configure(IEnumerable<AxisConfig> configs)
        {
            foreach (var config in configs)
            {
                _configs[config.AxisId] = config;

                // 按控制卡分组，同一张卡只创建一次
                if (!_cards.ContainsKey(config.CardId))
                {
                    IMotionCard card = config.CardType switch
                    {
                        MotionCardType.Simulation => new SimulationMotionCard(config.CardId),
                        // 其他控制卡类型的扩展点：
                        // MotionCardType.EtherCAT => new EtherCATMotionCard(config.CardId),
                        // MotionCardType.PLC => new PLCMotionCard(config.CardId),
                        _ => new SimulationMotionCard(config.CardId)
                    };
                    _cards[config.CardId] = card;
                }

                // 配置轴到控制卡
                if (_cards[config.CardId] is SimulationMotionCard simCard)
                {
                    simCard.ConfigureAxis(config);
                }

                // 建立轴ID → 控制器的映射
                _axes[config.AxisId] = _cards[config.CardId].GetAxis(config.CardAxisIndex);
            }
        }

        /// <summary>
        /// 初始化所有控制卡。
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (_isInitialized) return true;

            foreach (var card in _cards.Values)
            {
                if (!await card.InitializeAsync()) return false;
            }

            _isInitialized = true;
            return true;
        }

        /// <summary>
        /// 获取指定轴的控制器。
        /// </summary>
        /// <param name="axisId">轴ID</param>
        /// <returns>轴控制器（找不到返回null）</returns>
        public IAxisController? GetAxis(int axisId)
        {
            return _axes.TryGetValue(axisId, out var axis) ? axis : null;
        }

        /// <summary>
        /// 获取所有轴的状态。
        /// </summary>
        public IReadOnlyList<AxisStatus> GetAllStatus()
        {
            return _axes.Values.Select(a => a.Status).ToList().AsReadOnly();
        }

        /// <summary>
        /// 全部轴使能。
        /// </summary>
        public async Task<bool> ServoOnAllAsync()
        {
            foreach (var axis in _axes.Values)
            {
                if (!await axis.ServoOnAsync()) return false;
            }
            return true;
        }

        /// <summary>
        /// 全部轴关闭使能。
        /// </summary>
        public async Task ServoOffAllAsync()
        {
            foreach (var axis in _axes.Values)
            {
                await axis.ServoOffAsync();
            }
        }

        /// <summary>
        /// 全部轴急停 — 紧急停止所有运动。
        /// </summary>
        public async Task EmergencyStopAllAsync()
        {
            var tasks = _cards.Values.Select(c => c.EmergencyStopAllAsync());
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 全部轴回原点。
        /// </summary>
        public async Task<bool> HomeAllAsync(CancellationToken cancellationToken = default)
        {
            // 按轴ID顺序逐一回原点（工业设备通常有回原点顺序要求）
            foreach (var axisId in _axes.Keys.OrderBy(id => id))
            {
                if (cancellationToken.IsCancellationRequested) return false;
                if (!await _axes[axisId].HomeAsync(cancellationToken)) return false;
            }
            return true;
        }

        /// <summary>
        /// 直线插补运动 — 多轴同步运动。
        /// </summary>
        public async Task<bool> LinearMoveAsync(int[] axisIds, double[] positions, double velocity,
            CancellationToken cancellationToken = default)
        {
            // 找到这些轴所在的控制卡
            var firstAxisId = axisIds[0];
            if (!_configs.TryGetValue(firstAxisId, out var config)) return false;
            if (!_cards.TryGetValue(config.CardId, out var card)) return false;

            // 将轴ID转换为卡上轴号
            var cardAxes = axisIds.Select(id => _configs[id].CardAxisIndex).ToArray();

            return await card.LinearMoveAsync(cardAxes, positions, velocity, cancellationToken);
        }

        /// <summary>
        /// 创建默认的30轴配置（用于演示和学习）。
        /// 模拟一台典型半导体设备的轴配置。
        /// </summary>
        public static List<AxisConfig> CreateDefaultConfigs()
        {
            var configs = new List<AxisConfig>();
            var axisNames = new[]
            {
                // 卡0：搬运机器人（6轴）
                "搬运-X轴", "搬运-Y轴", "搬运-Z轴", "搬运-R轴", "搬运-U轴", "搬运-V轴",
                // 卡0：对位平台（4轴）
                "对位-X轴", "对位-Y轴", "对位-Z轴", "对位-Theta",
                // 卡1：点胶机构（4轴）
                "点胶-X轴", "点胶-Y轴", "点胶-Z轴", "点胶-R轴",
                // 卡1：检测平台（4轴）
                "检测-X轴", "检测-Y轴", "检测-Z轴", "检测-Theta",
                // 卡2：上料机构（3轴）
                "上料-X轴", "上料-Y轴", "上料-Z轴",
                // 卡2：下料机构（3轴）
                "下料-X轴", "下料-Y轴", "下料-Z轴",
                // 卡3：辅助轴（6轴）
                "辅助1", "辅助2", "辅助3", "辅助4", "辅助5", "辅助6"
            };

            for (int i = 0; i < axisNames.Length; i++)
            {
                configs.Add(new AxisConfig
                {
                    AxisId = i,
                    Name = axisNames[i],
                    CardId = i / 10,         // 每10轴一张卡
                    CardAxisIndex = i % 10,
                    MaxVelocity = 200.0,
                    MaxAcceleration = 1000.0,
                    MaxDeceleration = 1000.0,
                    SoftLimitPositive = 500.0,
                    SoftLimitNegative = -500.0,
                    CardType = MotionCardType.Simulation
                });
            }

            return configs;
        }

        public void Dispose()
        {
            foreach (var card in _cards.Values)
            {
                card.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
