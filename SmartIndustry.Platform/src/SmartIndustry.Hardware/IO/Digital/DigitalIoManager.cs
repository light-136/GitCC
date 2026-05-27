// ============================================================
// 文件：DigitalIoManager.cs
// 层级：硬件抽象层（Hardware Layer）> IO > Digital
// 职责：数字IO管理器。
//       管理多路数字输入/输出通道，提供：
//       1. 通道注册（带名称和配置的通道管理）
//       2. 上升沿/下降沿检测（状态变化事件）
//       3. 防抖处理（可配置消抖时间，过滤输入抖动）
//       4. 周期性轮询（定时器驱动，可配置轮询间隔）
//       5. 输入/输出通道分别管理
//
// 防抖算法：
//   记录每个输入通道的最后状态变化时间，
//   只有连续稳定（超过消抖时间）才确认状态变化并触发事件。
//   适合处理机械开关、光电传感器等抖动信号。
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Collections.Concurrent;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Models;
using SmartIndustry.Hardware;

namespace SmartIndustry.Hardware.IO.Digital
{
    /// <summary>
    /// 边沿类型枚举
    /// </summary>
    public enum EdgeType
    {
        /// <summary>上升沿（信号从 false→true）</summary>
        Rising = 0,
        /// <summary>下降沿（信号从 true→false）</summary>
        Falling = 1,
        /// <summary>任意变化（上升或下降）</summary>
        Both = 2
    }

    /// <summary>
    /// IO通道防抖配置
    /// </summary>
    public class DebouncedChannelConfig
    {
        /// <summary>通道地址</summary>
        public int Address { get; set; }

        /// <summary>通道名称（工程可读名称）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>通道类型（DI/DO）</summary>
        public IoChannelType Type { get; set; }

        /// <summary>消抖时间（ms，0=不消抖，直接响应）</summary>
        public int DebounceTimeMs { get; set; } = 10;

        /// <summary>是否反相（true=逻辑取反）</summary>
        public bool IsInverted { get; set; }
    }

    /// <summary>
    /// IO变化事件参数
    /// </summary>
    public class IoEdgeEventArgs : EventArgs
    {
        /// <summary>触发边沿的通道</summary>
        public IoChannel Channel { get; set; } = new();

        /// <summary>边沿类型</summary>
        public EdgeType Edge { get; set; }

        /// <summary>事件发生时间</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 单个通道的运行时状态（含防抖逻辑状态）
    /// </summary>
    internal class ChannelRuntimeState
    {
        /// <summary>通道配置</summary>
        public DebouncedChannelConfig Config { get; init; } = new();

        /// <summary>已确认的当前状态（防抖后）</summary>
        public bool ConfirmedValue { get; set; }

        /// <summary>原始采样值（未防抖）</summary>
        public bool RawValue { get; set; }

        /// <summary>原始值最后变化时间（用于防抖计时）</summary>
        public long RawValueChangedAt { get; set; }  // Environment.TickCount64 ms

        /// <summary>最后更新时间</summary>
        public DateTime LastUpdate { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 数字IO管理器。
    /// 统一管理输入/输出通道，提供防抖、边沿检测和周期轮询功能。
    ///
    /// 使用方式：
    ///   var manager = new DigitalIoManager(ioDevice, eventBus);
    ///   manager.RegisterChannel(new DebouncedChannelConfig { Address=0, Name="夹爪到位", DebounceTimeMs=20 });
    ///   manager.RisingEdge += (s, e) => Console.WriteLine($"上升沿：{e.Channel.Name}");
    ///   manager.StartPolling(10); // 10ms轮询
    /// </summary>
    public class DigitalIoManager : IDisposable
    {
        // ==================== 私有字段 ====================

        /// <summary>底层IO设备接口（读取/写入物理IO）</summary>
        private readonly IDigitalIoDevice _ioDevice;

        /// <summary>事件总线（发布IO变化领域事件）</summary>
        private readonly IEventBus _eventBus;

        /// <summary>通道运行时状态（Key=通道地址）</summary>
        private readonly ConcurrentDictionary<int, ChannelRuntimeState> _channels = new();

        /// <summary>轮询定时器</summary>
        private Timer? _pollingTimer;

        /// <summary>设备标识</summary>
        private readonly string _deviceId;

        // ==================== 事件 ====================

        /// <summary>数字输入上升沿触发（已经过防抖）</summary>
        public event EventHandler<IoEdgeEventArgs>? RisingEdge;

        /// <summary>数字输入下降沿触发（已经过防抖）</summary>
        public event EventHandler<IoEdgeEventArgs>? FallingEdge;

        /// <summary>任意通道状态变化（已经过防抖）</summary>
        public event EventHandler<IoEdgeEventArgs>? AnyEdge;

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造数字IO管理器
        /// </summary>
        /// <param name="ioDevice">底层IO设备</param>
        /// <param name="eventBus">事件总线</param>
        /// <param name="deviceId">设备标识</param>
        public DigitalIoManager(IDigitalIoDevice ioDevice, IEventBus eventBus, string deviceId = "DIO_0")
        {
            _ioDevice = ioDevice ?? throw new ArgumentNullException(nameof(ioDevice));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _deviceId = deviceId;
        }

        // ==================== 通道注册 ====================

        /// <summary>
        /// 注册一个通道（必须在启动轮询前注册）
        /// </summary>
        public void RegisterChannel(DebouncedChannelConfig config)
        {
            var state = new ChannelRuntimeState
            {
                Config = config,
                ConfirmedValue = false,
                RawValue = false
            };
            _channels[config.Address] = state;
        }

        /// <summary>
        /// 批量注册通道
        /// </summary>
        public void RegisterChannels(IEnumerable<DebouncedChannelConfig> configs)
        {
            foreach (var c in configs) RegisterChannel(c);
        }

        // ==================== 轮询控制 ====================

        /// <summary>
        /// 启动周期性轮询
        /// </summary>
        /// <param name="periodMs">轮询周期（ms，默认10ms）</param>
        public void StartPolling(int periodMs = 10)
        {
            if (periodMs < 1) throw new ArgumentException("轮询周期不能小于1ms");
            _pollingTimer?.Dispose();
            _pollingTimer = new Timer(PollingCallback, null, periodMs, periodMs);
        }

        /// <summary>
        /// 停止轮询
        /// </summary>
        public void StopPolling()
        {
            _pollingTimer?.Dispose();
            _pollingTimer = null;
        }

        // ==================== IO 读写 ====================

        /// <summary>
        /// 读取已确认（防抖后）的数字输入值
        /// </summary>
        public bool ReadInput(int address)
        {
            if (_channels.TryGetValue(address, out var state))
                return state.ConfirmedValue ^ state.Config.IsInverted;
            return _ioDevice.ReadDigitalInput(address);
        }

        /// <summary>
        /// 写数字输出
        /// </summary>
        public void WriteOutput(int address, bool value)
        {
            bool actualValue = value ^ GetChannelConfig(address)?.IsInverted ?? false;
            _ioDevice.WriteDigitalOutput(address, actualValue);

            // 更新本地缓存状态
            if (_channels.TryGetValue(address, out var state))
            {
                state.ConfirmedValue = value;
                state.LastUpdate = DateTime.Now;
            }
        }

        /// <summary>
        /// 获取所有已注册通道的当前快照
        /// </summary>
        public List<IoChannel> GetAllChannels()
        {
            return _channels.Values.Select(s => new IoChannel
            {
                Address = s.Config.Address,
                Name = s.Config.Name,
                Type = s.Config.Type,
                DigitalValue = s.ConfirmedValue,
                LastUpdate = s.LastUpdate,
                IsInverted = s.Config.IsInverted
            }).ToList();
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 轮询回调（定时器线程执行）。
        /// 对每个输入通道：
        ///   1. 读取原始值
        ///   2. 若与上次不同，记录变化时间（防抖计时开始）
        ///   3. 若已稳定超过 DebounceTimeMs，确认状态变化并触发边沿事件
        /// </summary>
        private void PollingCallback(object? state)
        {
            long now = Environment.TickCount64;

            foreach (var ch in _channels.Values)
            {
                if (ch.Config.Type != IoChannelType.DigitalInput) continue;

                // 读取原始值（含反相）
                bool rawVal = _ioDevice.ReadDigitalInput(ch.Config.Address);
                if (ch.Config.IsInverted) rawVal = !rawVal;

                // 检测原始值是否变化
                if (rawVal != ch.RawValue)
                {
                    ch.RawValue = rawVal;
                    ch.RawValueChangedAt = now;
                }

                // 防抖判断：原始值与已确认值不同，且已稳定 DebounceTimeMs
                if (rawVal != ch.ConfirmedValue)
                {
                    int elapsed = (int)(now - ch.RawValueChangedAt);
                    if (elapsed >= ch.Config.DebounceTimeMs)
                    {
                        bool oldVal = ch.ConfirmedValue;
                        ch.ConfirmedValue = rawVal;
                        ch.LastUpdate = DateTime.Now;

                        // 触发边沿事件
                        EdgeType edge = rawVal ? EdgeType.Rising : EdgeType.Falling;
                        var ioChannel = new IoChannel
                        {
                            Address = ch.Config.Address,
                            Name = ch.Config.Name,
                            Type = IoChannelType.DigitalInput,
                            DigitalValue = rawVal,
                            LastUpdate = ch.LastUpdate
                        };
                        var args = new IoEdgeEventArgs { Channel = ioChannel, Edge = edge };

                        if (rawVal) RisingEdge?.Invoke(this, args);
                        else FallingEdge?.Invoke(this, args);
                        AnyEdge?.Invoke(this, args);

                        // 通过 IEventBus 发布（解耦通知）
                        _ = _eventBus.PublishAsync(new HardwareIoChangedEvent(
                            _deviceId, ioChannel, edge.ToString()));
                    }
                }
            }
        }

        /// <summary>
        /// 获取通道配置（不存在返回 null）
        /// </summary>
        private DebouncedChannelConfig? GetChannelConfig(int address)
            => _channels.TryGetValue(address, out var s) ? s.Config : null;

        // ==================== IDisposable ====================

        /// <inheritdoc/>
        public void Dispose()
        {
            StopPolling();
        }
    }

    /// <summary>
    /// 数字IO设备接口（由 SimulatedIoDevice 或实际驱动实现）
    /// </summary>
    public interface IDigitalIoDevice
    {
        /// <summary>读取数字输入</summary>
        bool ReadDigitalInput(int address);
        /// <summary>写数字输出</summary>
        void WriteDigitalOutput(int address, bool value);
    }
}
