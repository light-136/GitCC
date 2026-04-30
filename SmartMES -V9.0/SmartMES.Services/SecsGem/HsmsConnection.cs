// ============================================================
// 文件：HsmsConnection.cs
// 用途：HSMS TCP 传输层 — 管理 TCP 连接、HSMS 状态机和消息收发
// 标准：SEMI E37 — High-Speed SECS Message Services
// 设计思路：
//   作为 HSMS 客户端（Active Mode），负责：
//   1. TCP 连接建立和断开
//   2. HSMS 状态机管理（NotConnected→NotSelected→Selected）
//   3. 消息帧的发送和接收（基于长度前缀的二进制帧）
//   4. 定时器管理（T3~T8）
//   5. 心跳检测（Linktest）
//   6. 事务管理（请求-响应匹配）
// ============================================================

using System.Net.Sockets;
using SmartMES.Core.Models;

namespace SmartMES.Services.SecsGem
{
    /// <summary>
    /// HSMS TCP 传输层 — 管理 HSMS 协议的 TCP 连接和消息收发。
    ///
    /// 状态机：
    ///   NotConnected → (TCP Connect) → NotSelected
    ///   NotSelected → (Select.req/rsp) → Selected
    ///   Selected → (Separate/Disconnect/T7) → NotConnected
    ///
    /// 线程模型：
    ///   - 接收线程：后台线程持续读取 TCP 流
    ///   - 心跳线程：定期发送 Linktest.req
    ///   - 主线程：发送消息和管理状态
    /// </summary>
    public class HsmsConnection : IDisposable
    {
        // TCP 客户端
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;

        // 后台线程
        private CancellationTokenSource? _cts;
        private Thread? _receiveThread;
        private Thread? _heartbeatThread;

        // 事务管理 — 等待回复的请求
        private readonly Dictionary<uint, TaskCompletionSource<HsmsFrame>> _pendingReplies = new();
        private readonly object _sendLock = new();
        private readonly object _stateLock = new();

        // 配置
        private readonly ushort _sessionId;
        private readonly HsmsTimerConfig _timerConfig;

        /// <summary>当前 HSMS 状态。</summary>
        public HsmsState State { get; private set; } = HsmsState.NotConnected;

        /// <summary>远程主机地址。</summary>
        public string Host { get; }

        /// <summary>远程端口。</summary>
        public int Port { get; }

        /// <summary>是否已连接。</summary>
        public bool IsConnected => _tcpClient?.Connected ?? false;

        /// <summary>消息接收事件 — 收到数据消息时触发。</summary>
        public event EventHandler<HsmsFrame>? MessageReceived;

        /// <summary>状态变更事件。</summary>
        public event EventHandler<HsmsState>? StateChanged;

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>连接断开事件。</summary>
        public event EventHandler? Disconnected;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="host">主机地址。</param>
        /// <param name="port">端口号。</param>
        /// <param name="sessionId">HSMS 会话 ID。</param>
        /// <param name="timerConfig">HSMS 定时器配置（可选）。</param>
        public HsmsConnection(string host, int port, ushort sessionId = 0,
                               HsmsTimerConfig? timerConfig = null)
        {
            Host = host;
            Port = port;
            _sessionId = sessionId;
            _timerConfig = timerConfig ?? new HsmsTimerConfig();
        }

        // ========== 连接管理 ==========

        /// <summary>
        /// 建立 TCP 连接并启动接收线程。
        /// </summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            if (State != HsmsState.NotConnected)
                throw new InvalidOperationException($"当前状态 {State} 不允许连接");

            Log($"[HSMS] 连接到 {Host}:{Port}...");

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(Host, Port, ct);
            _stream = _tcpClient.GetStream();

            SetState(HsmsState.NotSelected);
            Log("[HSMS] TCP 连接成功，状态 → NotSelected");

            // 启动接收线程
            _cts = new CancellationTokenSource();
            _receiveThread = new Thread(() => ReceiveLoop(_cts.Token))
            {
                IsBackground = true,
                Name = "HSMS-Receive"
            };
            _receiveThread.Start();

            // 启动 T7 超时监控
            _ = MonitorT7Async(_cts.Token);
        }

        /// <summary>
        /// 发送 Select.req 并等待 Select.rsp，完成 HSMS 握手。
        /// </summary>
        public async Task<bool> SelectAsync(CancellationToken ct = default)
        {
            if (State != HsmsState.NotSelected)
                throw new InvalidOperationException($"当前状态 {State} 不允许 Select");

            var req = HsmsFrame.CreateSelectReq(_sessionId);
            var rsp = await SendAndWaitAsync(req, (int)_timerConfig.T6.TotalMilliseconds, ct);

            if (rsp != null && rsp.Header.SType == HsmsMessageType.SelectRsp)
            {
                SetState(HsmsState.Selected);
                Log("[HSMS] Select 成功，状态 → Selected");

                // 启动心跳线程
                _heartbeatThread = new Thread(() => HeartbeatLoop(_cts!.Token))
                {
                    IsBackground = true,
                    Name = "HSMS-Heartbeat"
                };
                _heartbeatThread.Start();

                return true;
            }

            Log("[HSMS] Select 失败");
            return false;
        }

        /// <summary>
        /// 断开 HSMS 连接 — 发送 Separate.req 后关闭 TCP。
        /// </summary>
        public void Disconnect()
        {
            lock (_stateLock)
            {
                if (State == HsmsState.NotConnected) return;

                try
                {
                    // 尝试发送 Separate.req
                    if (State == HsmsState.Selected && _stream != null)
                    {
                        var sep = HsmsFrame.CreateSeparateReq(_sessionId);
                        SendFrame(sep);
                    }
                }
                catch { }

                _cts?.Cancel();
                _stream?.Dispose();
                _tcpClient?.Dispose();
                _stream = null;
                _tcpClient = null;

                SetState(HsmsState.NotConnected);
                Log("[HSMS] 连接已断开");
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        // ========== 消息收发 ==========

        /// <summary>
        /// 发送 HSMS 帧。
        /// </summary>
        public void SendFrame(HsmsFrame frame)
        {
            lock (_sendLock)
            {
                if (_stream == null || !IsConnected)
                    throw new InvalidOperationException("未连接");

                byte[] wireData = frame.ToWireFormat();
                _stream.Write(wireData, 0, wireData.Length);
                _stream.Flush();

                Log($"[HSMS] 发送：SType={frame.Header.SType}, " +
                    $"S{frame.Header.Stream}F{frame.Header.Function}, " +
                    $"SysBytes={frame.Header.SystemBytes}");
            }
        }

        /// <summary>
        /// 发送 SECS-II 数据消息。
        /// </summary>
        public void SendDataMessage(byte stream, byte function, bool wBit,
                                     byte[]? body = null, uint systemBytes = 0)
        {
            var frame = HsmsFrame.CreateDataMessage(_sessionId, stream, function,
                                                     wBit, body, systemBytes);
            SendFrame(frame);
        }

        /// <summary>
        /// 发送请求帧并等待响应（带超时）。
        /// 通过 SystemBytes 匹配请求和响应。
        /// </summary>
        public async Task<HsmsFrame?> SendAndWaitAsync(HsmsFrame request,
                                                         int timeoutMs, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<HsmsFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            uint sysBytes = request.Header.SystemBytes;

            lock (_pendingReplies)
            {
                _pendingReplies[sysBytes] = tcs;
            }

            try
            {
                SendFrame(request);

                // 等待响应或超时
                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                linkedCts.Token.Register(() => tcs.TrySetCanceled());

                return await tcs.Task;
            }
            catch (TaskCanceledException)
            {
                Log($"[HSMS] 等待响应超时：SysBytes={sysBytes}");
                return null;
            }
            finally
            {
                lock (_pendingReplies)
                {
                    _pendingReplies.Remove(sysBytes);
                }
            }
        }

        // ========== 接收循环 ==========

        /// <summary>
        /// 后台接收循环 — 持续从 TCP 流读取 HSMS 帧。
        /// 帧格式：[4字节长度] + [10字节头] + [消息体]
        /// </summary>
        private void ReceiveLoop(CancellationToken ct)
        {
            var headerBuf = new byte[4]; // 长度前缀缓冲区

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_stream == null || !IsConnected) break;

                    // 读取 4 字节长度前缀
                    if (!ReadExact(headerBuf, 4)) break;

                    int msgLen = (headerBuf[0] << 24) | (headerBuf[1] << 16) |
                                 (headerBuf[2] << 8) | headerBuf[3];

                    if (msgLen < 10)
                    {
                        Log($"[HSMS] 消息长度无效：{msgLen}");
                        break;
                    }

                    // 读取消息内容（头 + 体）
                    var msgBuf = new byte[msgLen];
                    if (!ReadExact(msgBuf, msgLen)) break;

                    // 解析消息头
                    var hdrBytes = new byte[10];
                    Array.Copy(msgBuf, 0, hdrBytes, 0, 10);
                    var header = HsmsHeader.FromBytes(hdrBytes);

                    // 提取消息体
                    int bodyLen = msgLen - 10;
                    var body = new byte[bodyLen > 0 ? bodyLen : 0];
                    if (bodyLen > 0)
                        Array.Copy(msgBuf, 10, body, 0, bodyLen);

                    var frame = new HsmsFrame { Header = header, Body = body };

                    Log($"[HSMS] 接收：SType={header.SType}, " +
                        $"S{header.Stream}F{header.Function}, " +
                        $"SysBytes={header.SystemBytes}");

                    // 处理接收到的帧
                    HandleReceivedFrame(frame);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Log($"[HSMS] 接收异常：{ex.Message}");
                        break;
                    }
                }
            }

            if (!ct.IsCancellationRequested)
            {
                Disconnect();
            }
        }

        /// <summary>
        /// 精确读取指定字节数。
        /// </summary>
        private bool ReadExact(byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = _stream!.Read(buffer, offset, count - offset);
                if (read == 0) return false; // 连接关闭
                offset += read;
            }
            return true;
        }

        /// <summary>
        /// 处理接收到的 HSMS 帧 — 区分控制消息和数据消息。
        /// </summary>
        private void HandleReceivedFrame(HsmsFrame frame)
        {
            switch (frame.Header.SType)
            {
                case HsmsMessageType.SelectReq:
                    // 被动方收到 Select 请求，回复 Select.rsp
                    var selectRsp = HsmsFrame.CreateSelectRsp(_sessionId, frame.Header.SystemBytes);
                    SendFrame(selectRsp);
                    SetState(HsmsState.Selected);
                    break;

                case HsmsMessageType.LinktestReq:
                    // 收到心跳请求，回复心跳响应
                    var ltRsp = HsmsFrame.CreateLinktestRsp(frame.Header.SystemBytes);
                    SendFrame(ltRsp);
                    break;

                case HsmsMessageType.SeparateReq:
                    // 对方请求断开
                    Log("[HSMS] 收到 Separate.req");
                    Disconnect();
                    break;

                case HsmsMessageType.DataMessage:
                    // 数据消息 — 触发事件或匹配等待中的请求
                    MessageReceived?.Invoke(this, frame);
                    break;

                default:
                    break;
            }

            // 检查是否有等待此 SystemBytes 的响应
            lock (_pendingReplies)
            {
                if (_pendingReplies.TryGetValue(frame.Header.SystemBytes, out var tcs))
                {
                    tcs.TrySetResult(frame);
                    _pendingReplies.Remove(frame.Header.SystemBytes);
                }
            }
        }

        // ========== 心跳循环 ==========

        /// <summary>
        /// 心跳循环 — 定期发送 Linktest.req 确认连接存活。
        /// 间隔为 T6 定时器值。
        /// </summary>
        private void HeartbeatLoop(CancellationToken ct)
        {
            int intervalMs = (int)_timerConfig.T6.TotalMilliseconds;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(Math.Max(intervalMs, 5000));
                    if (ct.IsCancellationRequested || State != HsmsState.Selected) break;

                    var req = HsmsFrame.CreateLinktestReq();
                    SendFrame(req);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        // ========== T7 超时监控 ==========

        /// <summary>
        /// T7 超时监控 — 进入 NotSelected 状态后，若 T7 时间内未完成 Select，则断开。
        /// </summary>
        private async Task MonitorT7Async(CancellationToken ct)
        {
            try
            {
                await Task.Delay((int)_timerConfig.T7.TotalMilliseconds, ct);
                if (State == HsmsState.NotSelected)
                {
                    Log("[HSMS] T7 超时 — 断开连接");
                    Disconnect();
                }
            }
            catch (OperationCanceledException) { }
        }

        // ========== 辅助 ==========

        private void SetState(HsmsState newState)
        {
            if (State != newState)
            {
                State = newState;
                StateChanged?.Invoke(this, newState);
            }
        }

        private void Log(string message)
        {
            MessageLogged?.Invoke(this, message);
        }

        public void Dispose()
        {
            Disconnect();
            _cts?.Dispose();
        }
    }
}
