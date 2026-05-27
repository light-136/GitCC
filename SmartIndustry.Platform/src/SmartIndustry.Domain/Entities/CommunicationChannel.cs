// ============================================================
// 文件：CommunicationChannel.cs
// 层次：领域层 (Domain Layer) — 实体
// 职责：表示一个通信通道的配置信息（TCP/串口/MQTT/Modbus等）
// 设计思路：
//   通信通道配置与运行时状态分开：配置持久化到数据库，
//   运行时状态（CurrentState、LastHeartbeatAt）由 Infrastructure 层动态更新。
//   ConnectionParameters 字段以 JSON 存储协议相关的差异化参数。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Entities
{
    /// <summary>
    /// 通信通道配置实体，对应数据库表 CommunicationChannels。
    /// 存储各类工业通信协议的连接参数和运行状态。
    /// </summary>
    public class CommunicationChannel : BaseEntity
    {
        // ----------------------------------------------------------------
        // 通道标识
        // ----------------------------------------------------------------

        /// <summary>通道名称（全局唯一，用于事件路由和日志标识）</summary>
        public string ChannelName { get; set; } = string.Empty;

        /// <summary>通道描述（说明此通道连接的设备和用途）</summary>
        public string Description { get; set; } = string.Empty;

        // ----------------------------------------------------------------
        // 协议配置
        // ----------------------------------------------------------------

        /// <summary>通信协议类型（决定 Infrastructure 层实例化哪个驱动）</summary>
        public CommunicationProtocol Protocol { get; set; } = CommunicationProtocol.TcpClient;

        /// <summary>目标IP地址或主机名（TCP/MQTT/OPC UA 协议使用）</summary>
        public string? HostAddress { get; set; }

        /// <summary>目标端口号（TCP/MQTT/OPC UA 协议使用）</summary>
        public int Port { get; set; }

        /// <summary>串口名称（COM1/ttyS0，串口协议使用）</summary>
        public string? SerialPortName { get; set; }

        /// <summary>串口波特率（串口协议使用）</summary>
        public int BaudRate { get; set; } = 9600;

        /// <summary>
        /// 协议差异化参数（JSON格式）。
        /// TCP: {"Timeout": 5000, "MaxRetry": 3}
        /// MQTT: {"ClientId": "...", "Topic": "...", "QoS": 1}
        /// Modbus: {"SlaveId": 1, "FunctionCode": 3}
        /// </summary>
        public string ConnectionParameters { get; set; } = "{}";

        // ----------------------------------------------------------------
        // 重连配置
        // ----------------------------------------------------------------

        /// <summary>是否启用自动重连（断连后按 ReconnectIntervalMs 间隔尝试重连）</summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>自动重连间隔（毫秒，默认5000ms）</summary>
        public int ReconnectIntervalMs { get; set; } = 5000;

        /// <summary>最大重连次数（0=无限重试，其他值=超过后停止重试并报警）</summary>
        public int MaxReconnectAttempts { get; set; } = 0;

        // ----------------------------------------------------------------
        // 心跳配置
        // ----------------------------------------------------------------

        /// <summary>是否启用心跳包（定期发送心跳维持连接并检测对端存活）</summary>
        public bool HeartbeatEnabled { get; set; } = true;

        /// <summary>心跳间隔（毫秒，默认30000ms=30秒）</summary>
        public int HeartbeatIntervalMs { get; set; } = 30000;

        // ----------------------------------------------------------------
        // 运行时状态（动态更新，持久化最后已知状态）
        // ----------------------------------------------------------------

        /// <summary>当前连接状态（由 Infrastructure 层驱动器实时更新）</summary>
        public ConnectionState CurrentState { get; set; } = ConnectionState.Disconnected;

        /// <summary>最后一次成功收发心跳/数据的时间（UTC，用于超时检测）</summary>
        public DateTime? LastHeartbeatAt { get; set; }

        /// <summary>累计接收字节数（用于流量统计）</summary>
        public long TotalBytesReceived { get; set; } = 0;

        /// <summary>累计发送字节数</summary>
        public long TotalBytesSent { get; set; } = 0;
    }
}
