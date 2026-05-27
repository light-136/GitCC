// ============================================================
// 文件：SimulatedIoDevice.cs
// 层级：硬件抽象层（Hardware Layer）> IO > Simulation
// 职责：模拟IO设备，实现 IDigitalIoDevice 接口。
//       用于开发/测试阶段无真实 PLC 时的 IO 仿真。
//       提供16路DI + 16路DO + 4路AI + 4路AO。
//
// 模拟行为：
//   - 模拟输入（DI）：可手动设置（测试用），也可开启随机游走
//   - 模拟输出（DO）：直接存储写入值，可被读回
//   - 模拟输入（AI）：支持随机游走（±0.1V/s）模拟传感器信号
//   - 模拟输出（AO）：直接存储写入值
//   - ChannelChanged 事件：当 DI 或 AI 值变化时触发
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Collections.Concurrent;
using SmartIndustry.Hardware.IO.Digital;

namespace SmartIndustry.Hardware.IO.Simulation
{
    /// <summary>
    /// 模拟IO通道（内部状态存储）
    /// </summary>
    public class SimIoChannel
    {
        public int Address { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool DigitalValue { get; set; }
        public double AnalogValue { get; set; }
        public DateTime LastUpdate { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 模拟IO设备 — 16DI + 16DO + 4AI + 4AO 的纯软件仿真。
    ///
    /// IO地址映射：
    ///   DI : 地址 0  ~ 15
    ///   DO : 地址 100 ~ 115
    ///   AI : 地址 200 ~ 203
    ///   AO : 地址 300 ~ 303
    ///
    /// 使用方式：
    ///   var device = new SimulatedIoDevice("SimIO");
    ///   device.StartSimulation(); // 启动模拟输入随机变化
    ///   // 手动设置输入（测试用）：
    ///   device.SetDigitalInput(0, true);   // DI0=ON
    ///   device.SetAnalogInput(0, 5.0);     // AI0=5.0V
    /// </summary>
    public class SimulatedIoDevice : IDigitalIoDevice, IDisposable
    {
        // ==================== 地址范围常量 ====================
        private const int DiStart = 0, DiEnd = 15;
        private const int DoStart = 100, DoEnd = 115;
        private const int AiStart = 200, AiEnd = 203;
        private const int AoStart = 300, AoEnd = 303;

        // ==================== 私有字段 ====================

        /// <summary>设备名称</summary>
        private readonly string _deviceName;

        /// <summary>所有IO通道状态（Key=地址）</summary>
        private readonly ConcurrentDictionary<int, SimIoChannel> _channels = new();

        /// <summary>随机数生成器（AI游走噪声）</summary>
        private readonly Random _random = new();

        /// <summary>模拟定时器（驱动输入随机变化）</summary>
        private Timer? _simTimer;

        /// <summary>是否已释放</summary>
        private bool _disposed;

        // ==================== 事件 ====================

        /// <summary>某个通道状态发生变化时触发（DI状态翻转、AI值超阈值变化）</summary>
        public event EventHandler<SimIoChannel>? ChannelChanged;

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造模拟IO设备，自动初始化所有通道
        /// </summary>
        /// <param name="deviceName">设备名称</param>
        public SimulatedIoDevice(string deviceName = "SimIO_0")
        {
            _deviceName = deviceName;
            InitializeChannels();
        }

        // ==================== 公开属性 ====================

        /// <summary>设备名称</summary>
        public string DeviceName => _deviceName;

        // ==================== IDigitalIoDevice 实现 ====================

        /// <summary>
        /// 读取数字输入状态（DI，地址 0~15）
        /// </summary>
        public bool ReadDigitalInput(int address)
        {
            if (!IsValidDiAddress(address)) return false;
            return _channels.TryGetValue(address, out var ch) && ch.DigitalValue;
        }

        /// <summary>
        /// 写数字输出（DO，地址 100~115）
        /// </summary>
        public void WriteDigitalOutput(int address, bool value)
        {
            if (!IsValidDoAddress(address)) return;
            if (!_channels.TryGetValue(address, out var ch)) return;
            bool changed = ch.DigitalValue != value;
            ch.DigitalValue = value;
            ch.LastUpdate = DateTime.Now;
            if (changed) ChannelChanged?.Invoke(this, ch);
        }

        // ==================== 扩展读写方法 ====================

        /// <summary>
        /// 读取模拟输入电压（AI，地址 200~203，返回 0~10V）
        /// </summary>
        public double ReadAnalogInput(int address)
        {
            if (!IsValidAiAddress(address)) return 0;
            return _channels.TryGetValue(address, out var ch) ? ch.AnalogValue : 0;
        }

        /// <summary>
        /// 写模拟输出（AO，地址 300~303，值 0~10V）
        /// </summary>
        public void WriteAnalogOutput(int address, double value)
        {
            if (!IsValidAoAddress(address)) return;
            if (!_channels.TryGetValue(address, out var ch)) return;
            ch.AnalogValue = Math.Clamp(value, 0.0, 10.0);
            ch.LastUpdate = DateTime.Now;
            ChannelChanged?.Invoke(this, ch);
        }

        /// <summary>
        /// 读取数字输出当前状态（用于状态回读）
        /// </summary>
        public bool ReadDigitalOutput(int address)
        {
            if (!IsValidDoAddress(address)) return false;
            return _channels.TryGetValue(address, out var ch) && ch.DigitalValue;
        }

        // ==================== 测试辅助方法 ====================

        /// <summary>
        /// 手动设置数字输入状态（测试用，模拟传感器触发）
        /// </summary>
        public void SetDigitalInput(int address, bool value)
        {
            if (!IsValidDiAddress(address)) return;
            if (!_channels.TryGetValue(address, out var ch)) return;
            bool changed = ch.DigitalValue != value;
            ch.DigitalValue = value;
            ch.LastUpdate = DateTime.Now;
            if (changed) ChannelChanged?.Invoke(this, ch);
        }

        /// <summary>
        /// 手动设置模拟输入值（测试用，模拟传感器信号）
        /// </summary>
        public void SetAnalogInput(int address, double value)
        {
            if (!IsValidAiAddress(address)) return;
            if (!_channels.TryGetValue(address, out var ch)) return;
            ch.AnalogValue = Math.Clamp(value, 0.0, 10.0);
            ch.LastUpdate = DateTime.Now;
            ChannelChanged?.Invoke(this, ch);
        }

        // ==================== 模拟控制 ====================

        /// <summary>
        /// 启动模拟输入自动变化（模拟传感器正常信号波动）。
        /// DI：每2秒随机翻转1~2路（概率10%）
        /// AI：每500ms进行随机游走（±0.2V步进）
        /// </summary>
        /// <param name="periodMs">模拟更新周期（ms，默认500）</param>
        public void StartSimulation(int periodMs = 500)
        {
            _simTimer?.Dispose();
            _simTimer = new Timer(SimulationCallback, null, periodMs, periodMs);
        }

        /// <summary>停止自动模拟</summary>
        public void StopSimulation()
        {
            _simTimer?.Dispose();
            _simTimer = null;
        }

        /// <summary>
        /// 获取所有通道的状态快照（调试用）
        /// </summary>
        public IReadOnlyList<(int Address, string Name, bool DigitalVal, double AnalogVal)> GetAllChannels()
        {
            return _channels.Values
                .OrderBy(c => c.Address)
                .Select(c => (c.Address, c.Name, c.DigitalValue, c.AnalogValue))
                .ToList();
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 初始化所有通道（16DI + 16DO + 4AI + 4AO）
        /// </summary>
        private void InitializeChannels()
        {
            // 数字输入 DI0~DI15
            for (int i = DiStart; i <= DiEnd; i++)
                _channels[i] = new SimIoChannel { Address = i, Name = $"DI{i:D2}" };

            // 数字输出 DO0~DO15（地址100~115）
            for (int i = DoStart; i <= DoEnd; i++)
                _channels[i] = new SimIoChannel { Address = i, Name = $"DO{(i - DoStart):D2}" };

            // 模拟输入 AI0~AI3（地址200~203）
            for (int i = AiStart; i <= AiEnd; i++)
                _channels[i] = new SimIoChannel { Address = i, Name = $"AI{(i - AiStart)}", AnalogValue = 5.0 };

            // 模拟输出 AO0~AO3（地址300~303）
            for (int i = AoStart; i <= AoEnd; i++)
                _channels[i] = new SimIoChannel { Address = i, Name = $"AO{(i - AoStart)}" };
        }

        /// <summary>
        /// 模拟定时器回调：DI随机翻转 + AI随机游走
        /// </summary>
        private void SimulationCallback(object? state)
        {
            // DI：每路有5%概率发生状态翻转（模拟开关/传感器抖动）
            for (int addr = DiStart; addr <= DiEnd; addr++)
            {
                if (_random.NextDouble() < 0.05 && _channels.TryGetValue(addr, out var ch))
                {
                    ch.DigitalValue = !ch.DigitalValue;
                    ch.LastUpdate = DateTime.Now;
                    ChannelChanged?.Invoke(this, ch);
                }
            }

            // AI：随机游走（±0.2V步进，限制在0~10V）
            for (int addr = AiStart; addr <= AiEnd; addr++)
            {
                if (_channels.TryGetValue(addr, out var ch))
                {
                    double delta = (_random.NextDouble() - 0.5) * 0.4; // ±0.2V
                    double newVal = Math.Clamp(ch.AnalogValue + delta, 0.0, 10.0);
                    if (Math.Abs(newVal - ch.AnalogValue) > 0.01)
                    {
                        ch.AnalogValue = Math.Round(newVal, 3);
                        ch.LastUpdate = DateTime.Now;
                        ChannelChanged?.Invoke(this, ch);
                    }
                }
            }
        }

        // 地址合法性检查
        private static bool IsValidDiAddress(int a) => a >= DiStart && a <= DiEnd;
        private static bool IsValidDoAddress(int a) => a >= DoStart && a <= DoEnd;
        private static bool IsValidAiAddress(int a) => a >= AiStart && a <= AiEnd;
        private static bool IsValidAoAddress(int a) => a >= AoStart && a <= AoEnd;

        // ==================== IDisposable ====================

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _simTimer?.Dispose();
        }
    }
}
