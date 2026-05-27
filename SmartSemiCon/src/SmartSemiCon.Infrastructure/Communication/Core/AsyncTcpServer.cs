// ============================================================
// 文件：AsyncTcpServer.cs
// 用途：工业级异步TCP服务端
// 设计思路：
//   作为Server端监听客户端连接，支持多客户端并发。
//   适用于：上位机作为Server等待PLC/控制器连接的场景。
//   每个客户端连接在独立线程中处理。
// ============================================================

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SmartSemiCon.Domain.Interfaces;

namespace SmartSemiCon.Infrastructure.Communication.Core
{
    /// <summary>
    /// 工业级异步TCP服务端 — 监听并管理多个客户端连接。
    /// </summary>
    public class AsyncTcpServer : ITcpServer
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _listenerCts;

        // 活跃客户端会话：ClientId → TcpClient
        private readonly ConcurrentDictionary<string, TcpClient> _clients = new();

        /// <summary>是否正在监听</summary>
        public bool IsListening { get; private set; }

        /// <summary>当前连接的客户端数量</summary>
        public int ClientCount => _clients.Count;

        /// <summary>接收缓冲区大小</summary>
        public int ReceiveBufferSize { get; set; } = 8192;

        /// <summary>新客户端连接事件</summary>
        public event EventHandler<string>? ClientConnected;

        /// <summary>客户端断开事件</summary>
        public event EventHandler<string>? ClientDisconnected;

        /// <summary>收到客户端数据事件</summary>
        public event EventHandler<(string ClientId, byte[] Data)>? DataReceived;

        /// <summary>
        /// 启动服务端监听。
        /// </summary>
        /// <param name="port">监听端口号</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            if (IsListening) return;

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            IsListening = true;

            _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 启动接受连接循环
            _ = Task.Run(async () =>
            {
                while (!_listenerCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _listener.AcceptTcpClientAsync(_listenerCts.Token);
                        var clientId = client.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();

                        _clients[clientId] = client;
                        ClientConnected?.Invoke(this, clientId);

                        // 为每个客户端启动独立的接收循环
                        _ = Task.Run(() => HandleClientAsync(clientId, client, _listenerCts.Token));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // 接受连接失败，继续等待下一个
                    }
                }
            });

            await Task.CompletedTask;
        }

        /// <summary>
        /// 停止服务端监听。
        /// </summary>
        public async Task StopAsync()
        {
            _listenerCts?.Cancel();

            // 关闭所有客户端连接
            foreach (var (clientId, client) in _clients)
            {
                try { client.Close(); } catch { }
                ClientDisconnected?.Invoke(this, clientId);
            }
            _clients.Clear();

            _listener?.Stop();
            IsListening = false;

            await Task.CompletedTask;
        }

        /// <summary>
        /// 向所有客户端广播数据。
        /// </summary>
        public async Task BroadcastAsync(byte[] data)
        {
            var tasks = new List<Task>();
            foreach (var (clientId, client) in _clients)
            {
                tasks.Add(SendToClientAsync(clientId, data));
            }
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 向指定客户端发送数据。
        /// </summary>
        public async Task<bool> SendToClientAsync(string clientId, byte[] data)
        {
            if (!_clients.TryGetValue(clientId, out var client)) return false;

            try
            {
                var stream = client.GetStream();
                await stream.WriteAsync(data);
                await stream.FlushAsync();
                return true;
            }
            catch
            {
                RemoveClient(clientId);
                return false;
            }
        }

        /// <summary>
        /// 处理单个客户端的数据接收循环。
        /// </summary>
        private async Task HandleClientAsync(string clientId, TcpClient client, CancellationToken cancellationToken)
        {
            var buffer = new byte[ReceiveBufferSize];

            try
            {
                var stream = client.GetStream();

                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead == 0)
                    {
                        // 客户端断开
                        break;
                    }

                    var receivedData = new byte[bytesRead];
                    Array.Copy(buffer, receivedData, bytesRead);

                    DataReceived?.Invoke(this, (clientId, receivedData));
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                RemoveClient(clientId);
            }
        }

        /// <summary>
        /// 移除客户端连接。
        /// </summary>
        private void RemoveClient(string clientId)
        {
            if (_clients.TryRemove(clientId, out var client))
            {
                try { client.Close(); } catch { }
                ClientDisconnected?.Invoke(this, clientId);
            }
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }
    }
}
