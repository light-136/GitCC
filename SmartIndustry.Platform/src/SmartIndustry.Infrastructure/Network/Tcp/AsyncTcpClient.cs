// ============================================================
// 文件：AsyncTcpClient.cs
// 层次：基础设施层 (Infrastructure Layer) — 工业级异步 TCP 客户端
// 职责：
//   提供可靠的异步 TCP 客户端，支持：
//   - 自动重连（可配置间隔和最大次数）
//   - 心跳包（定期发送空包保持连接活跃并检测对端存活）
//   - 命令-响应匹配（ConcurrentDictionary + TaskCompletionSource + 超时控制）
//   - Length-Prefix 粘包/拆包处理（PacketBuilder）
//   - 线程安全的发送队列（Channel<byte[]>）
//   - 完整的连接状态事件通知
// 设计思路：
//   工业通信的可靠性要求远高于普通应用。连接断开必须自动恢复，
//   心跳超时必须触发重连，消息必须有序且不丢失。
//   发送队列使用 System.Threading.Channels（无锁 MPSC 队列）替代 lock+Queue，
//   显著减少高频发送时的线程竞争。
//   命令-响应匹配通过 RequestId 字段关联请求与响应（类似 HTTP 请求/响应模型），
//   上层代码调用 SendRequestAsync 后 await 等待特定 RequestId 的响应。
// 注意：
//   此客户端绑定 Length-Prefix 协议（PacketProtocol），
//   对端服务器必须使用相同协议帧格式。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Events;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Enums;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;

namespace SmartIndustry.Infrastructure.Network.Tcp
{
    // ----------------------------------------------------------------
    // TCP 客户端配置选项
    // ----------------------------------------------------------------

    /// <summary>
    /// AsyncTcpClient 配置参数
    /// </summary>
    public class TcpClientOptions
    {
        /// <summary>服务器主机名或 IP 地址</summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>服务器端口号</summary>
        public int Port { get; set; } = 502;

        /// <summary>通道名称（用于日志和事件路由标识）</summary>
        public string ChannelName { get; set; } = "TcpClient";

        /// <summary>连接超时时间（毫秒，默认5秒）</summary>
        public int ConnectTimeoutMs { get; set; } = 5000;

        /// <summary>是否启用自动重连（默认启用）</summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>重连间隔时间（毫秒，默认3秒）</summary>
        public int ReconnectIntervalMs { get; set; } = 3000;

        /// <summary>最大重连次数（0=无限重试，默认0）</summary>
        public int MaxReconnectAttempts { get; set; } = 0;

        /// <summary>是否启用心跳（默认启用）</summary>
        public bool HeartbeatEnabled { get; set; } = true;

        /// <summary>心跳间隔时间（毫秒，默认30秒）</summary>
        public int HeartbeatIntervalMs { get; set; } = 30_000;

        /// <summary>心跳超时时间（毫秒，超过此时间未收到任何数据则断开重连，默认60秒）</summary>
        public int HeartbeatTimeoutMs { get; set; } = 60_000;

        /// <summary>命令-响应默认超时时间（毫秒，默认5秒）</summary>
        public int RequestTimeoutMs { get; set; } = 5000;

        /// <summary>接收缓冲区大小（字节，默认64KB）</summary>
        public int ReceiveBufferSize { get; set; } = 65536;

        /// <summary>发送队列容量上限（超过后发送请求会等待，防止内存无限增长）</summary>
        public int SendQueueCapacity { get; set; } = 1000;
    }

    /// <summary>
    /// 工业级异步 TCP 客户端。
    /// 实现连接管理、自动重连、心跳、粘包处理、命令-响应匹配。
    /// </summary>
    public class AsyncTcpClient : IDisposable
    {
        // ----------------------------------------------------------------
        // 依赖和配置
        // ----------------------------------------------------------------

        private readonly TcpClientOptions _options;
        private readonly IEventBus _eventBus;

        // ----------------------------------------------------------------
        // 连接状态
        // ----------------------------------------------------------------

        /// <summary>当前连接状态（线程安全读取，写入通过 UpdateState 方法）</summary>
        private volatile ConnectionState _state = ConnectionState.Disconnected;

        /// <summary>当前 TCP 连接对象（null=未连接）</summary>
        private TcpClient? _tcpClient;

        /// <summary>网络流（用于收发数据）</summary>
        private NetworkStream? _networkStream;

        // ----------------------------------------------------------------
        // 线程安全的发送队列（System.Threading.Channels）
        // Channel 是 .NET Core 引入的高性能无锁消息管道，比 ConcurrentQueue + SemaphoreSlim 更高效
        // ----------------------------------------------------------------

        /// <summary>待发送数据队列（bounded=有界，防止内存无限增长）</summary>
        private Channel<byte[]>? _sendChannel;

        // ----------------------------------------------------------------
        // 粘包/拆包处理
        // ----------------------------------------------------------------

        /// <summary>数据包构建器（维护接收缓冲区，处理粘包/拆包）</summary>
        private readonly PacketBuilder _packetBuilder = new();

        // ----------------------------------------------------------------
        // 命令-响应匹配（RequestId -> TaskCompletionSource）
        // ----------------------------------------------------------------

        /// <summary>
        /// 待响应的请求字典。
        /// Key=请求ID（由调用方生成的唯一标识符），Value=TaskCompletionSource（等待响应）。
        /// 收到响应帧后，根据 RequestId 找到对应的 TCS，SetResult 唤醒等待方。
        /// </summary>
        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new();

        // ----------------------------------------------------------------
        // 生命周期控制
        // ----------------------------------------------------------------

        /// <summary>主 CancellationToken（Dispose 时取消，终止所有后台任务）</summary>
        private readonly CancellationTokenSource _lifetimeCts = new();

        /// <summary>当前连接专用的 CancellationToken（断开时取消，重连时重新创建）</summary>
        private CancellationTokenSource? _connectionCts;

        /// <summary>上次收到数据的时间（用于心跳超时检测）</summary>
        private DateTime _lastReceivedAt = DateTime.UtcNow;

        /// <summary>累计重连次数（用于实现最大重连次数限制）</summary>
        private int _reconnectAttempts = 0;

        // ----------------------------------------------------------------
        // 并发控制：防止多个线程同时执行连接/断开操作
        // ----------------------------------------------------------------
        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        // ----------------------------------------------------------------
        // 外部事件
        // ----------------------------------------------------------------

        /// <summary>连接状态变更事件（新状态）</summary>
        public event Action<ConnectionState>? StateChanged;

        /// <summary>收到完整数据包事件（消息体字节数组，不含长度头）</summary>
        public event Action<byte[]>? DataReceived;

        // ----------------------------------------------------------------
        // 只读属性
        // ----------------------------------------------------------------

        /// <summary>当前连接状态（只读）</summary>
        public ConnectionState State => _state;

        /// <summary>是否已连接</summary>
        public bool IsConnected => _state == ConnectionState.Connected;

        /// <summary>通道名称</summary>
        public string ChannelName => _options.ChannelName;

        /// <summary>
        /// 构造函数
        /// </summary>
        public AsyncTcpClient(TcpClientOptions options, IEventBus eventBus)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        // ================================================================
        // 连接管理
        // ================================================================

        /// <summary>
        /// 主动发起连接（如果已连接则直接返回成功）。
        /// 成功后启动接收循环和发送循环后台任务。
        /// </summary>
        /// <param name="cancellationToken">外部取消令牌</param>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            // 防止并发连接
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                if (_state == ConnectionState.Connected) return true;

                return await DoConnectAsync(cancellationToken);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        /// <summary>
        /// 主动断开连接（停止所有后台任务，清理资源）
        /// </summary>
        public async Task DisconnectAsync()
        {
            await _connectionLock.WaitAsync();
            try
            {
                await DoDisconnectAsync("主动断开");
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        // ================================================================
        // 数据发送
        // ================================================================

        /// <summary>
        /// 发送原始字节数据（自动添加 Length-Prefix 帧头）。
        /// 数据加入发送队列后立即返回（非阻塞）。
        /// </summary>
        public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _sendChannel == null)
                throw new InvalidOperationException("TCP 客户端未连接，无法发送数据");

            var frame = PacketProtocol.WrapMessage(data);

            // 写入发送队列（如果队列满则等待）
            await _sendChannel.Writer.WriteAsync(frame, cancellationToken);
        }

        /// <summary>
        /// 发送请求并等待对应 RequestId 的响应（命令-响应模式）。
        /// 调用方需要在 data 中包含 RequestId 字段，响应帧也需要回传相同的 RequestId。
        /// 具体的 RequestId 解析逻辑由上层协议实现（此方法提供通用基础设施）。
        /// </summary>
        /// <param name="requestId">请求的唯一标识符（应由调用方生成 GUID）</param>
        /// <param name="data">请求数据字节</param>
        /// <param name="timeoutMs">等待响应超时时间（毫秒，0=使用默认配置）</param>
        public async Task<byte[]> SendRequestAsync(string requestId, byte[] data, int timeoutMs = 0)
        {
            var timeout = timeoutMs > 0 ? timeoutMs : _options.RequestTimeoutMs;

            // 创建 TaskCompletionSource 用于等待响应
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[requestId] = tcs;

            try
            {
                // 发送请求
                await SendAsync(data);

                // 等待响应（超时后取消等待）
                using var timeoutCts = new CancellationTokenSource(timeout);
                timeoutCts.Token.Register(() =>
                {
                    tcs.TrySetException(new TimeoutException(
                        $"请求 {requestId} 在 {timeout}ms 内未收到响应"));
                });

                return await tcs.Task;
            }
            finally
            {
                // 无论成功还是超时，都清理挂起请求字典
                _pendingRequests.TryRemove(requestId, out _);
            }
        }

        /// <summary>
        /// 完成挂起请求（收到响应帧时，由接收处理逻辑调用）。
        /// 上层协议解析器从响应帧中提取 RequestId 后调用此方法。
        /// </summary>
        public void CompleteRequest(string requestId, byte[] responseData)
        {
            if (_pendingRequests.TryGetValue(requestId, out var tcs))
                tcs.TrySetResult(responseData);
        }

        // ================================================================
        // 私有实现：连接/断开/接收/发送循环
        // ================================================================

        /// <summary>执行实际连接操作（被 ConnectAsync 调用，已持有连接锁）</summary>
        private async Task<bool> DoConnectAsync(CancellationToken cancellationToken)
        {
            UpdateState(ConnectionState.Connecting);

            try
            {
                // 创建新的 TcpClient 实例
                _tcpClient = new TcpClient();
                _tcpClient.ReceiveBufferSize = _options.ReceiveBufferSize;
                _tcpClient.SendBufferSize = _options.ReceiveBufferSize;

                // 带超时的连接（连接超时触发 OperationCanceledException）
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _lifetimeCts.Token);
                connectCts.CancelAfter(_options.ConnectTimeoutMs);

                await _tcpClient.ConnectAsync(_options.Host, _options.Port, connectCts.Token);

                _networkStream = _tcpClient.GetStream();
                _lastReceivedAt = DateTime.UtcNow;
                _reconnectAttempts = 0;

                // 为本次连接创建独立的 CancellationToken（断开时取消）
                _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);

                // 创建有界发送队列
                _sendChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(_options.SendQueueCapacity)
                {
                    // 队列满时等待（背压机制，避免无限积压）
                    FullMode = BoundedChannelFullMode.Wait,
                    // 单个消费者（发送循环）
                    SingleReader = true
                });

                // 清空拆包器（丢弃上次连接残留的未完整帧）
                _packetBuilder.Clear();

                // 启动后台任务：接收循环、发送循环、心跳循环
                _ = Task.Run(() => ReceiveLoopAsync(_connectionCts.Token), _connectionCts.Token);
                _ = Task.Run(() => SendLoopAsync(_connectionCts.Token), _connectionCts.Token);
                if (_options.HeartbeatEnabled)
                    _ = Task.Run(() => HeartbeatLoopAsync(_connectionCts.Token), _connectionCts.Token);

                UpdateState(ConnectionState.Connected);
                return true;
            }
            catch (Exception ex)
            {
                UpdateState(ConnectionState.Error);
                CleanupTcpResources();

                // 如果启用自动重连，启动重连任务
                if (_options.AutoReconnect && !_lifetimeCts.IsCancellationRequested)
                    _ = Task.Run(() => ReconnectLoopAsync());

                return false;
            }
        }

        /// <summary>执行断开操作（被 DisconnectAsync 或错误处理调用）</summary>
        private Task DoDisconnectAsync(string reason)
        {
            _connectionCts?.Cancel();
            CleanupTcpResources();
            _sendChannel?.Writer.TryComplete();

            // 取消所有挂起的请求
            foreach (var kv in _pendingRequests)
                kv.Value.TrySetException(new InvalidOperationException($"连接断开：{reason}"));
            _pendingRequests.Clear();

            UpdateState(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 接收循环后台任务：持续从 NetworkStream 读取数据，通过 PacketBuilder 拆包，
        /// 将完整包传递给 DataReceived 事件处理器和挂起请求完成器。
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[_options.ReceiveBufferSize];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var bytesRead = await _networkStream!.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead == 0)
                    {
                        // 对端优雅关闭连接（ReadAsync 返回0表示 FIN）
                        break;
                    }

                    // 更新最后接收时间（心跳超时检测使用）
                    _lastReceivedAt = DateTime.UtcNow;

                    // 将新数据追加到拆包器
                    _packetBuilder.Append(buffer, 0, bytesRead);

                    // 循环取出所有完整的消息包（处理粘包：一次 Receive 可能包含多个完整包）
                    byte[]? packet;
                    while ((packet = _packetBuilder.TryGetNextPacket()) != null)
                    {
                        // 触发数据接收事件
                        DataReceived?.Invoke(packet);

                        // 发布领域事件到事件总线
                        await _eventBus.PublishAsync(new DataReceivedEvent(_options.ChannelName, packet));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消（DisconnectAsync 或 Dispose 触发），不处理
            }
            catch (Exception)
            {
                // 接收异常（网络断开等），触发重连
                UpdateState(ConnectionState.Error);
            }
            finally
            {
                // 接收循环结束，如果不是主动取消则尝试重连
                if (!_lifetimeCts.IsCancellationRequested && _options.AutoReconnect)
                    _ = Task.Run(() => ReconnectLoopAsync());
            }
        }

        /// <summary>
        /// 发送循环后台任务：从发送队列读取数据帧，写入 NetworkStream。
        /// 使用队列保证发送顺序，避免多线程并发写入网络流导致数据交错。
        /// </summary>
        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var frame in _sendChannel!.Reader.ReadAllAsync(cancellationToken))
                {
                    await _networkStream!.WriteAsync(frame, cancellationToken);
                    await _networkStream.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，不处理
            }
            catch (Exception)
            {
                // 发送异常，接收循环会检测到连接断开并触发重连
            }
        }

        /// <summary>
        /// 心跳循环后台任务：定期发送心跳包，检测超时并触发重连。
        /// 心跳包为空消息体（0字节负载），接收方只需返回任意数据即可重置超时计时器。
        /// </summary>
        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(_options.HeartbeatIntervalMs, cancellationToken);

                    if (!IsConnected) break;

                    // 检查心跳超时（超过 HeartbeatTimeoutMs 未收到任何数据则断开）
                    var elapsed = (DateTime.UtcNow - _lastReceivedAt).TotalMilliseconds;
                    if (elapsed > _options.HeartbeatTimeoutMs)
                    {
                        // 心跳超时，主动断开并触发重连
                        UpdateState(ConnectionState.Error);
                        _connectionCts?.Cancel();
                        break;
                    }

                    // 发送心跳包（空消息体）
                    try
                    {
                        await SendAsync(Array.Empty<byte>(), cancellationToken);
                    }
                    catch
                    {
                        // 心跳发送失败，接收循环会处理重连
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
        }

        /// <summary>
        /// 自动重连循环（启用 AutoReconnect 时，连接断开后自动执行）。
        /// 按 ReconnectIntervalMs 间隔等待后尝试重新连接。
        /// </summary>
        private async Task ReconnectLoopAsync()
        {
            while (!_lifetimeCts.IsCancellationRequested)
            {
                // 检查重连次数限制
                if (_options.MaxReconnectAttempts > 0 && _reconnectAttempts >= _options.MaxReconnectAttempts)
                {
                    // 达到最大重连次数，停止重连
                    UpdateState(ConnectionState.Error);
                    return;
                }

                _reconnectAttempts++;

                // 等待重连间隔
                try
                {
                    await Task.Delay(_options.ReconnectIntervalMs, _lifetimeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                // 尝试重连
                await _connectionLock.WaitAsync(_lifetimeCts.Token);
                try
                {
                    if (_state == ConnectionState.Connected) return; // 已经被其他路径重连成功

                    CleanupTcpResources();
                    var success = await DoConnectAsync(_lifetimeCts.Token);

                    if (success) return; // 重连成功，退出循环
                }
                finally
                {
                    _connectionLock.Release();
                }
            }
        }

        /// <summary>
        /// 更新连接状态：线程安全地设置 _state 字段，发布状态变更事件
        /// </summary>
        private void UpdateState(ConnectionState newState)
        {
            var old = _state;
            _state = newState;

            if (old != newState)
            {
                StateChanged?.Invoke(newState);
                _ = _eventBus.PublishAsync(new CommunicationStateChangedEvent(
                    _options.ChannelName, old, newState));
            }
        }

        /// <summary>清理 TCP 连接相关资源（不影响重连能力）</summary>
        private void CleanupTcpResources()
        {
            _connectionCts?.Cancel();
            _networkStream?.Dispose();
            _tcpClient?.Dispose();
            _networkStream = null;
            _tcpClient = null;
            _packetBuilder.Clear();
        }

        // ================================================================
        // IDisposable 实现
        // ================================================================

        private bool _disposed = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _lifetimeCts.Cancel();
            CleanupTcpResources();
            _sendChannel?.Writer.TryComplete();
            _connectionLock.Dispose();
            _lifetimeCts.Dispose();
            _connectionCts?.Dispose();
        }
    }
}
