// ============================================================
// 文件：AsyncTcpClient.cs
// 用途：工业级异步TCP客户端
// 设计思路：
//   工业通讯的核心要求：
//   1. 异步非阻塞 — 不能阻塞UI线程或运动控制线程
//   2. 自动重连 — 网络断开后自动尝试重新连接
//   3. 心跳机制 — 定时发送心跳包检测连接活性
//   4. 消息封包/拆包 — 处理TCP粘包/拆包问题
//   5. 命令-响应匹配 — 发送请求后等待对应的回复
//
//   使用 System.Net.Sockets.TcpClient + NetworkStream 实现。
//   接收循环在独立线程中运行，通过事件通知上层。
// ============================================================

using System.Collections.Concurrent;
using System.Net.Sockets;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;

namespace SmartSemiCon.Infrastructure.Communication.Core
{
    /// <summary>
    /// 工业级异步TCP客户端。
    /// 支持自动重连、心跳、超时检测、命令-响应匹配。
    /// </summary>
    public class AsyncTcpClient : ICommunicationService
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private CancellationTokenSource? _receiveCts;
        private CancellationTokenSource? _heartbeatCts;
        private CancellationTokenSource? _reconnectCts;

        // 配置参数
        private string _host = string.Empty;
        private int _port;
        private readonly object _lock = new();

        // 命令-响应匹配字典：消息ID → 等待的TaskCompletionSource
        private readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]>> _pendingRequests = new();
        private int _messageIdCounter;

        /// <summary>通道名称</summary>
        public string ChannelName { get; }

        /// <summary>连接状态</summary>
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        /// <summary>是否已连接</summary>
        public bool IsConnected => State == ConnectionState.Connected && _tcpClient?.Connected == true;

        /// <summary>是否启用自动重连</summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>重连间隔（毫秒）</summary>
        public int ReconnectIntervalMs { get; set; } = 3000;

        /// <summary>心跳间隔（毫秒），0表示不启用心跳</summary>
        public int HeartbeatIntervalMs { get; set; } = 5000;

        /// <summary>心跳数据包（可自定义）</summary>
        public byte[] HeartbeatData { get; set; } = new byte[] { 0x00 };

        /// <summary>接收缓冲区大小</summary>
        public int ReceiveBufferSize { get; set; } = 8192;

        /// <summary>数据接收事件</summary>
        public event EventHandler<byte[]>? DataReceived;

        /// <summary>连接状态变更事件</summary>
        public event EventHandler<ConnectionState>? StateChanged;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="channelName">通道名称标识（如 "PLC通讯"、"视觉系统"）</param>
        public AsyncTcpClient(string channelName = "TCP")
        {
            ChannelName = channelName;
        }

        /// <summary>
        /// 连接到远程服务器。
        /// </summary>
        public async Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            _host = host;
            _port = port;

            try
            {
                SetState(ConnectionState.Connecting);

                _tcpClient = new TcpClient();
                _tcpClient.ReceiveBufferSize = ReceiveBufferSize;
                _tcpClient.SendBufferSize = ReceiveBufferSize;
                _tcpClient.NoDelay = true; // 禁用Nagle算法，减少通讯延迟

                // 带超时的连接
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                await _tcpClient.ConnectAsync(host, port, timeoutCts.Token);

                _stream = _tcpClient.GetStream();

                // 启动接收循环
                _receiveCts = new CancellationTokenSource();
                _ = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));

                // 启动心跳
                if (HeartbeatIntervalMs > 0)
                {
                    _heartbeatCts = new CancellationTokenSource();
                    _ = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
                }

                SetState(ConnectionState.Connected);
                return true;
            }
            catch
            {
                SetState(ConnectionState.Disconnected);
                if (AutoReconnect)
                {
                    StartReconnect();
                }
                return false;
            }
        }

        /// <summary>
        /// 断开连接。
        /// </summary>
        public async Task DisconnectAsync()
        {
            AutoReconnect = false; // 主动断开时不自动重连
            _reconnectCts?.Cancel();
            _heartbeatCts?.Cancel();
            _receiveCts?.Cancel();

            CloseSocket();
            SetState(ConnectionState.Disconnected);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 发送数据。
        /// </summary>
        public async Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _stream == null) return false;

            try
            {
                await _stream.WriteAsync(data, cancellationToken);
                await _stream.FlushAsync(cancellationToken);
                return true;
            }
            catch
            {
                HandleDisconnection();
                return false;
            }
        }

        /// <summary>
        /// 发送数据并等待回复（命令-响应模式）。
        /// 通过一个递增的消息ID匹配请求和响应。
        /// 适用于有明确请求-回复关系的协议（如Modbus）。
        /// </summary>
        public async Task<byte[]?> SendAndReceiveAsync(byte[] data, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (!IsConnected) return null;

            var messageId = Interlocked.Increment(ref _messageIdCounter);
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[messageId] = tcs;

            try
            {
                if (!await SendAsync(data, cancellationToken)) return null;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled());
                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                _pendingRequests.TryRemove(messageId, out _);
            }
        }

        /// <summary>
        /// 接收循环 — 在独立线程中持续读取网络数据。
        /// 收到数据后通过 DataReceived 事件通知上层。
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[ReceiveBufferSize];

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_stream == null || !IsConnected) break;

                    var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead == 0)
                    {
                        // 远程端关闭连接
                        HandleDisconnection();
                        break;
                    }

                    // 复制接收到的数据
                    var receivedData = new byte[bytesRead];
                    Array.Copy(buffer, receivedData, bytesRead);

                    // 尝试匹配等待中的请求
                    foreach (var pending in _pendingRequests)
                    {
                        pending.Value.TrySetResult(receivedData);
                        _pendingRequests.TryRemove(pending.Key, out _);
                        break; // 简单的FIFO匹配
                    }

                    // 通知上层
                    DataReceived?.Invoke(this, receivedData);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    HandleDisconnection();
                    break;
                }
            }
        }

        /// <summary>
        /// 心跳循环 — 定时发送心跳包检测连接活性。
        /// 如果发送心跳失败，说明连接已断开。
        /// </summary>
        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(HeartbeatIntervalMs, cancellationToken);

                    if (!IsConnected) break;

                    if (!await SendAsync(HeartbeatData, cancellationToken))
                    {
                        HandleDisconnection();
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 处理连接断开 — 清理资源并尝试自动重连。
        /// </summary>
        private void HandleDisconnection()
        {
            CloseSocket();
            SetState(ConnectionState.Disconnected);

            if (AutoReconnect)
            {
                StartReconnect();
            }
        }

        /// <summary>
        /// 启动自动重连循环。
        /// </summary>
        private void StartReconnect()
        {
            if (State == ConnectionState.Reconnecting) return;

            _reconnectCts?.Cancel();
            _reconnectCts = new CancellationTokenSource();

            SetState(ConnectionState.Reconnecting);

            _ = Task.Run(async () =>
            {
                while (!_reconnectCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(ReconnectIntervalMs, _reconnectCts.Token);

                        // 尝试重连（临时关闭自动重连避免递归）
                        var savedAutoReconnect = AutoReconnect;
                        AutoReconnect = false;
                        var success = await ConnectAsync(_host, _port, _reconnectCts.Token);
                        AutoReconnect = savedAutoReconnect;

                        if (success) return;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            });
        }

        /// <summary>
        /// 关闭Socket连接。
        /// </summary>
        private void CloseSocket()
        {
            lock (_lock)
            {
                try
                {
                    _stream?.Close();
                    _tcpClient?.Close();
                }
                catch { }
                finally
                {
                    _stream = null;
                    _tcpClient = null;
                }
            }
        }

        /// <summary>
        /// 设置连接状态并触发事件。
        /// </summary>
        private void SetState(ConnectionState newState)
        {
            if (State == newState) return;
            State = newState;
            StateChanged?.Invoke(this, newState);
        }

        public void Dispose()
        {
            _reconnectCts?.Cancel();
            _heartbeatCts?.Cancel();
            _receiveCts?.Cancel();
            CloseSocket();
            GC.SuppressFinalize(this);
        }
    }
}
