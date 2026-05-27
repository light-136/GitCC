// ============================================================
// 文件：AsyncTcpServer.cs
// 层次：基础设施层 (Infrastructure Layer) — 工业级异步 TCP 服务端
// 职责：
//   提供多客户端 TCP 服务端，支持：
//   - 并发多客户端连接管理（ConcurrentDictionary）
//   - 客户端连接/断开事件通知
//   - 广播发送（向所有连接的客户端发送）和定向发送
//   - 与 AsyncTcpClient 相同的 Length-Prefix 协议
//   - 最大连接数限制
//   - 每个客户端独立的接收/发送循环（基于 PacketBuilder）
// 设计思路：
//   TcpListener 接受连接后，为每个客户端创建独立的 ClientSession 对象，
//   管理该客户端的 NetworkStream、PacketBuilder 和发送队列。
//   ConcurrentDictionary<Guid, ClientSession> 存储所有活跃会话，
//   广播时并发遍历所有会话发送，无需加锁。
//   服务端架构适合：SCADA 上位机（被 PLC/设备连接）、多工位协同通信中枢。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Events;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Enums;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace SmartIndustry.Infrastructure.Network.Tcp
{
    // ----------------------------------------------------------------
    // TCP 服务端配置
    // ----------------------------------------------------------------

    /// <summary>
    /// AsyncTcpServer 配置参数
    /// </summary>
    public class TcpServerOptions
    {
        /// <summary>监听 IP 地址（"0.0.0.0"=所有网卡，"127.0.0.1"=仅本机）</summary>
        public string ListenAddress { get; set; } = "0.0.0.0";

        /// <summary>监听端口号</summary>
        public int Port { get; set; } = 9000;

        /// <summary>通道名称（日志标识）</summary>
        public string ChannelName { get; set; } = "TcpServer";

        /// <summary>最大客户端连接数（0=不限制，默认100）</summary>
        public int MaxClients { get; set; } = 100;

        /// <summary>接收缓冲区大小（字节，默认64KB）</summary>
        public int ReceiveBufferSize { get; set; } = 65536;

        /// <summary>单个客户端发送队列容量上限（默认500）</summary>
        public int SendQueueCapacity { get; set; } = 500;
    }

    // ----------------------------------------------------------------
    // 客户端会话（每个连接的客户端对应一个实例）
    // ----------------------------------------------------------------

    /// <summary>
    /// 单个 TCP 客户端的会话信息（服务端视角）
    /// </summary>
    public class ClientSession
    {
        /// <summary>会话唯一标识符（每次连接生成新 GUID）</summary>
        public Guid SessionId { get; } = Guid.NewGuid();

        /// <summary>客户端远端地址（IP:Port 格式，用于日志和鉴权）</summary>
        public string RemoteEndPoint { get; init; } = string.Empty;

        /// <summary>连接建立时间（UTC）</summary>
        public DateTime ConnectedAt { get; } = DateTime.UtcNow;

        /// <summary>TCP 客户端对象</summary>
        internal TcpClient TcpClient { get; init; } = null!;

        /// <summary>网络流（读写数据）</summary>
        internal NetworkStream NetworkStream { get; init; } = null!;

        /// <summary>拆包器（每个客户端独立的缓冲区）</summary>
        internal PacketBuilder PacketBuilder { get; } = new();

        /// <summary>该客户端的发送队列</summary>
        internal Channel<byte[]> SendChannel { get; init; } = null!;

        /// <summary>该客户端的连接生命周期取消令牌（断开时取消）</summary>
        internal CancellationTokenSource Cts { get; } = new();
    }

    /// <summary>
    /// 工业级异步 TCP 服务端。
    /// 管理多个客户端连接，提供广播和定向发送能力。
    /// </summary>
    public class AsyncTcpServer : IDisposable
    {
        // ----------------------------------------------------------------
        // 依赖和配置
        // ----------------------------------------------------------------

        private readonly TcpServerOptions _options;
        private readonly IEventBus _eventBus;

        // ----------------------------------------------------------------
        // 服务端核心组件
        // ----------------------------------------------------------------

        /// <summary>TCP 监听器（等待客户端连接）</summary>
        private TcpListener? _listener;

        /// <summary>所有活跃客户端会话（SessionId -> ClientSession）</summary>
        private readonly ConcurrentDictionary<Guid, ClientSession> _sessions = new();

        /// <summary>生命周期取消令牌（Stop() 或 Dispose() 时触发）</summary>
        private readonly CancellationTokenSource _lifetimeCts = new();

        // ----------------------------------------------------------------
        // 状态
        // ----------------------------------------------------------------

        private volatile bool _isRunning = false;

        /// <summary>服务端是否正在运行（监听中）</summary>
        public bool IsRunning => _isRunning;

        /// <summary>当前连接的客户端数量</summary>
        public int ClientCount => _sessions.Count;

        // ----------------------------------------------------------------
        // 外部事件
        // ----------------------------------------------------------------

        /// <summary>新客户端连接事件（ClientSession 包含连接信息）</summary>
        public event Action<ClientSession>? ClientConnected;

        /// <summary>客户端断开事件（ClientSession, 断开原因）</summary>
        public event Action<ClientSession, string>? ClientDisconnected;

        /// <summary>收到客户端数据事件（ClientSession, 消息体字节数组）</summary>
        public event Action<ClientSession, byte[]>? DataReceived;

        /// <summary>
        /// 构造函数
        /// </summary>
        public AsyncTcpServer(TcpServerOptions options, IEventBus eventBus)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        // ================================================================
        // 服务端生命周期
        // ================================================================

        /// <summary>
        /// 启动 TCP 服务端（开始监听指定端口）。
        /// 启动接受连接的后台任务后立即返回。
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning) return Task.CompletedTask;

            var listenIp = IPAddress.Parse(_options.ListenAddress);
            _listener = new TcpListener(listenIp, _options.Port);
            _listener.Start();
            _isRunning = true;

            // 启动接受连接的后台任务
            _ = Task.Run(() => AcceptLoopAsync(_lifetimeCts.Token), _lifetimeCts.Token);

            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止服务端（断开所有客户端连接，停止监听）
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _lifetimeCts.Cancel();
            _listener?.Stop();

            // 断开所有客户端
            foreach (var session in _sessions.Values)
            {
                await DisconnectClientAsync(session, "服务端关闭");
            }
            _sessions.Clear();
        }

        // ================================================================
        // 数据发送
        // ================================================================

        /// <summary>
        /// 广播数据到所有已连接的客户端（并发发送，不等待结果）
        /// </summary>
        public async Task BroadcastAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (!_isRunning) return;

            var frame = PacketProtocol.WrapMessage(data);

            // 并发向所有客户端的发送队列写入
            var sendTasks = _sessions.Values
                .Select(session => SendToSessionAsync(session, frame, cancellationToken));

            await Task.WhenAll(sendTasks);
        }

        /// <summary>
        /// 向指定客户端（按 SessionId）发送数据
        /// </summary>
        public async Task SendToAsync(Guid sessionId, byte[] data, CancellationToken cancellationToken = default)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new InvalidOperationException($"客户端 Session {sessionId} 不存在");

            var frame = PacketProtocol.WrapMessage(data);
            await SendToSessionAsync(session, frame, cancellationToken);
        }

        // ================================================================
        // 私有实现
        // ================================================================

        /// <summary>
        /// 接受连接循环：持续接受新的 TCP 客户端连接。
        /// 每个新连接创建 ClientSession 并启动独立的接收/发送后台任务。
        /// </summary>
        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _listener!.AcceptTcpClientAsync(cancellationToken);

                    // 检查连接数限制
                    if (_options.MaxClients > 0 && _sessions.Count >= _options.MaxClients)
                    {
                        // 超过最大连接数，拒绝新连接
                        tcpClient.Dispose();
                        continue;
                    }

                    // 创建新的客户端会话
                    var session = new ClientSession
                    {
                        TcpClient = tcpClient,
                        NetworkStream = tcpClient.GetStream(),
                        RemoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown",
                        SendChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(_options.SendQueueCapacity)
                        {
                            FullMode = BoundedChannelFullMode.Wait,
                            SingleReader = true
                        })
                    };

                    _sessions[session.SessionId] = session;

                    // 通知客户端连接事件
                    ClientConnected?.Invoke(session);
                    await _eventBus.PublishAsync(new CommunicationStateChangedEvent(
                        _options.ChannelName,
                        ConnectionState.Disconnected,
                        ConnectionState.Connected));

                    // 为新客户端启动独立的接收和发送后台任务
                    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        session.Cts.Token, cancellationToken);

                    _ = Task.Run(() => ClientReceiveLoopAsync(session, linkedCts.Token));
                    _ = Task.Run(() => ClientSendLoopAsync(session, linkedCts.Token));
                }
                catch (OperationCanceledException)
                {
                    break; // 服务端正常关闭
                }
                catch (Exception)
                {
                    // 接受连接时的临时错误（如文件描述符耗尽），稍后重试
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        /// <summary>单个客户端的接收循环（逻辑与 AsyncTcpClient 的 ReceiveLoopAsync 类似）</summary>
        private async Task ClientReceiveLoopAsync(ClientSession session, CancellationToken cancellationToken)
        {
            var buffer = new byte[_options.ReceiveBufferSize];
            string disconnectReason = "正常断开";

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var bytesRead = await session.NetworkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead == 0)
                    {
                        disconnectReason = "对端关闭连接";
                        break;
                    }

                    session.PacketBuilder.Append(buffer, 0, bytesRead);

                    byte[]? packet;
                    while ((packet = session.PacketBuilder.TryGetNextPacket()) != null)
                    {
                        DataReceived?.Invoke(session, packet);
                        await _eventBus.PublishAsync(new DataReceivedEvent(_options.ChannelName, packet));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                disconnectReason = "服务端关闭";
            }
            catch (Exception ex)
            {
                disconnectReason = $"异常：{ex.Message}";
            }
            finally
            {
                await DisconnectClientAsync(session, disconnectReason);
            }
        }

        /// <summary>单个客户端的发送循环（从发送队列读取并写入网络流）</summary>
        private async Task ClientSendLoopAsync(ClientSession session, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var frame in session.SendChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    await session.NetworkStream.WriteAsync(frame, cancellationToken);
                    await session.NetworkStream.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* 发送异常，接收循环会处理断开 */ }
        }

        /// <summary>向指定会话的发送队列写入帧（已封包）</summary>
        private static async Task SendToSessionAsync(ClientSession session, byte[] frame,
            CancellationToken cancellationToken)
        {
            try
            {
                await session.SendChannel.Writer.WriteAsync(frame, cancellationToken);
            }
            catch
            {
                // 队列已关闭（客户端已断开），静默忽略
            }
        }

        /// <summary>断开指定客户端，清理资源，通知事件</summary>
        private async Task DisconnectClientAsync(ClientSession session, string reason)
        {
            if (!_sessions.TryRemove(session.SessionId, out _)) return; // 已处理过

            session.Cts.Cancel();
            session.SendChannel.Writer.TryComplete();
            session.NetworkStream.Dispose();
            session.TcpClient.Dispose();

            ClientDisconnected?.Invoke(session, reason);
            await _eventBus.PublishAsync(new CommunicationStateChangedEvent(
                _options.ChannelName,
                ConnectionState.Connected,
                ConnectionState.Disconnected));
        }

        // ================================================================
        // IDisposable
        // ================================================================

        public void Dispose()
        {
            _ = StopAsync();
            _listener?.Stop();
            _lifetimeCts.Dispose();
        }
    }
}
