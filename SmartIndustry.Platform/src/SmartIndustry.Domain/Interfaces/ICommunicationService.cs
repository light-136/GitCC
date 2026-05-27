// ============================================================
// 文件：ICommunicationService.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义与外部设备（PLC、机器人、上位机）通信的服务接口
// 设计思路：
//   ICommunicationService 是通信功能的高层门面接口，支持：
//     1. 面向连接的通信（ConnectAsync/DisconnectAsync）
//     2. 单向发送（SendAsync）和同步请求-应答（SendAndReceiveAsync）
//     3. 异步数据接收（OnDataReceived 事件，由底层驱动在收到完整帧时触发）
//   协议差异由 Infrastructure 层的具体实现封装（TCP 粘包处理、Modbus 帧格式等）。
//   一个 CommunicationChannel 实体对应一个 ICommunicationService 实例。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 通信服务接口（面向单个通信通道的完整操作契约）。
    /// </summary>
    public interface ICommunicationService : IAsyncDisposable
    {
        // ----------------------------------------------------------------
        // 通道标识
        // ----------------------------------------------------------------

        /// <summary>对应的通信通道名称（与 CommunicationChannel.Name 一致）</summary>
        string ChannelName { get; }

        /// <summary>当前连接状态</summary>
        bool IsConnected { get; }

        // ----------------------------------------------------------------
        // 连接管理
        // ----------------------------------------------------------------

        /// <summary>
        /// 异步建立通信连接（TCP 三次握手 / 串口打开 / MQTT 连接）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌（超时或取消时抛出 OperationCanceledException）</param>
        /// <returns>true 表示连接成功</returns>
        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步断开通信连接（优雅关闭，发送 FIN 或关闭串口）。
        /// </summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 数据收发
        // ----------------------------------------------------------------

        /// <summary>
        /// 单向异步发送数据（Fire-and-Forget，不等待响应）。
        /// </summary>
        /// <param name="data">要发送的字节数组</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>true 表示数据已写入发送缓冲区</returns>
        Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default);

        /// <summary>
        /// 请求-应答模式（发送后等待响应，用于 Modbus 读写、自定义协议查询）。
        /// </summary>
        /// <param name="requestData">请求数据帧</param>
        /// <param name="timeoutMs">等待响应的超时时间（毫秒）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>收到的响应数据帧（超时或连接断开时返回 null）</returns>
        Task<byte[]?> SendAndReceiveAsync(
            byte[] requestData,
            int timeoutMs = 1000,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 数据接收事件（由底层驱动在收到完整数据帧时触发）
        // ----------------------------------------------------------------

        /// <summary>
        /// 数据接收事件。
        /// 每次收到一个完整数据帧时触发（粘包处理后）。
        /// 订阅方应快速返回，避免阻塞接收线程。
        /// 参数：(channelName: 通道名称, data: 接收到的完整帧字节数组)
        /// </summary>
        event Action<string, byte[]> OnDataReceived;
    }
}
