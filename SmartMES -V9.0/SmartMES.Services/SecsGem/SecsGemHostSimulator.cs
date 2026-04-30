// ============================================================
// 文件：SecsGemHostSimulator.cs
// 用途：模拟 GEM 主机 — 用于测试设备端 SECS/GEM 实现
// 设计思路：
//   在没有真实 MES 主机的情况下，提供一个模拟主机用于：
//   1. 接受设备端的 TCP 连接
//   2. 响应 Select.req/Linktest.req
//   3. 处理 S1F13 建立通信 → 回复 S1F14
//   4. 处理 S1F17 请求上线 → 回复 S1F18
//   5. 响应 S1F1 心跳 → 回复 S1F2
//   6. 接收 S6F11 事件报告 → 回复 S6F12
//   7. 接收 S5F1 告警报告 → 回复 S5F2
//   8. 可以主动发送 S2F41 远程命令
//   9. 可以查询 SV/EC/PP
//
//   模拟主机运行在独立线程，监听指定端口。
// ============================================================

using System.Net;
using System.Net.Sockets;
using SmartMES.Core.Models;

namespace SmartMES.Services.SecsGem
{
    /// <summary>
    /// 模拟 GEM 主机 — TCP 服务端，用于测试设备端的 SECS/GEM 实现。
    ///
    /// 使用方式：
    ///   var host = new SecsGemHostSimulator(5000);
    ///   host.Start();
    ///   // ... 设备连接并交互 ...
    ///   host.Stop();
    /// </summary>
    public class SecsGemHostSimulator : IDisposable
    {
        private TcpListener? _listener;
        private TcpClient? _clientConnection;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private Thread? _listenThread;
        private Thread? _receiveThread;
        private readonly object _sendLock = new();

        private readonly int _port;
        private readonly ushort _sessionId;

        /// <summary>是否有设备连接。</summary>
        public bool IsDeviceConnected => _clientConnection?.Connected ?? false;

        /// <summary>是否正在运行。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>收到的消息日志。</summary>
        public List<string> ReceivedMessages { get; } = new();

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>收到设备消息事件。</summary>
        public event EventHandler<HsmsFrame>? DeviceMessageReceived;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="port">监听端口。</param>
        /// <param name="sessionId">会话 ID。</param>
        public SecsGemHostSimulator(int port, ushort sessionId = 0)
        {
            _port = port;
            _sessionId = sessionId;
        }

        /// <summary>
        /// 启动模拟主机 — 开始监听 TCP 连接。
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            IsRunning = true;

            Log($"[模拟主机] 启动，监听端口 {_port}");

            _listenThread = new Thread(() => ListenLoop(_cts.Token))
            {
                IsBackground = true,
                Name = "GemHost-Listen"
            };
            _listenThread.Start();
        }

        /// <summary>
        /// 停止模拟主机。
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            _cts?.Cancel();
            _listener?.Stop();
            _clientConnection?.Dispose();
            _stream?.Dispose();
            IsRunning = false;

            Log("[模拟主机] 已停止");
        }

        /// <summary>
        /// 监听循环 — 等待设备连接。
        /// </summary>
        private void ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 等待设备连接
                    _clientConnection = _listener!.AcceptTcpClient();
                    _stream = _clientConnection.GetStream();

                    Log("[模拟主机] 设备已连接");

                    // 启动接收线程
                    _receiveThread = new Thread(() => ReceiveLoop(ct))
                    {
                        IsBackground = true,
                        Name = "GemHost-Receive"
                    };
                    _receiveThread.Start();
                    _receiveThread.Join(); // 等待断开后再接受新连接

                    Log("[模拟主机] 设备已断开");
                }
                catch (SocketException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Log($"[模拟主机] 监听异常：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 接收循环 — 读取设备发送的 HSMS 帧。
        /// </summary>
        private void ReceiveLoop(CancellationToken ct)
        {
            var headerBuf = new byte[4];

            while (!ct.IsCancellationRequested && IsDeviceConnected)
            {
                try
                {
                    // 读取4字节长度前缀
                    if (!ReadExact(headerBuf, 4)) break;

                    int msgLen = (headerBuf[0] << 24) | (headerBuf[1] << 16) |
                                 (headerBuf[2] << 8) | headerBuf[3];
                    if (msgLen < 10) break;

                    var msgBuf = new byte[msgLen];
                    if (!ReadExact(msgBuf, msgLen)) break;

                    var hdrBytes = new byte[10];
                    Array.Copy(msgBuf, 0, hdrBytes, 0, 10);
                    int bodyLen = msgLen - 10;
                    var body = new byte[bodyLen > 0 ? bodyLen : 0];
                    if (bodyLen > 0) Array.Copy(msgBuf, 10, body, 0, bodyLen);

                    var frame = new HsmsFrame
                    {
                        Header = HsmsHeader.FromBytes(hdrBytes),
                        Body = body
                    };

                    Log($"[模拟主机] 收到：SType={frame.Header.SType}, " +
                        $"S{frame.Header.Stream}F{frame.Header.Function}");

                    HandleDeviceMessage(frame);
                    DeviceMessageReceived?.Invoke(this, frame);
                }
                catch (Exception) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Log($"[模拟主机] 接收异常：{ex.Message}");
                    break;
                }
            }
        }

        /// <summary>
        /// 处理设备消息 — 按协议自动回复。
        /// </summary>
        private void HandleDeviceMessage(HsmsFrame frame)
        {
            switch (frame.Header.SType)
            {
                case HsmsMessageType.SelectReq:
                    // 回复 Select.rsp
                    SendFrame(HsmsFrame.CreateSelectRsp(_sessionId, frame.Header.SystemBytes));
                    Log("[模拟主机] Select 握手完成");
                    break;

                case HsmsMessageType.LinktestReq:
                    // 回复 Linktest.rsp
                    SendFrame(HsmsFrame.CreateLinktestRsp(frame.Header.SystemBytes));
                    break;

                case HsmsMessageType.SeparateReq:
                    Log("[模拟主机] 收到 Separate，断开连接");
                    break;

                case HsmsMessageType.DataMessage:
                    HandleDataMessage(frame);
                    break;
            }
        }

        /// <summary>
        /// 处理 SECS-II 数据消息。
        /// </summary>
        private void HandleDataMessage(HsmsFrame frame)
        {
            byte stream = frame.Header.Stream;
            byte function = frame.Header.Function;
            string msgId = $"S{stream}F{function}";

            // 解码消息体
            SecsItem? body = null;
            if (frame.Body.Length > 0)
            {
                try { body = SecsIICodec.Decode(frame.Body); }
                catch { }
            }

            if (body != null)
            {
                string sml = SecsIICodec.ToSml(body);
                ReceivedMessages.Add($"{msgId}\n{sml}");
            }

            switch (stream)
            {
                case 1 when function == 1:
                    // S1F1 心跳 → S1F2
                    ReplyWithBody(frame, 1, 2, BuildHostIdentity());
                    break;

                case 1 when function == 13:
                    // S1F13 建立通信 → S1F14（接受）
                    var s1f14 = SecsItem.CreateList();
                    s1f14.Children.Add(SecsItem.CreateBinary(new byte[] { 0 })); // COMMACK=0
                    var mdln = SecsItem.CreateList();
                    mdln.Children.Add(SecsItem.CreateAscii("HostSimulator"));
                    mdln.Children.Add(SecsItem.CreateAscii("V1.0"));
                    s1f14.Children.Add(mdln);
                    ReplyWithBody(frame, 1, 14, s1f14);
                    Log("[模拟主机] 通信建立（S1F13→S1F14）");
                    break;

                case 1 when function == 17:
                    // S1F17 请求上线 → S1F18（接受：ONLACK=0）
                    ReplyWithBody(frame, 1, 18, SecsItem.CreateBinary(new byte[] { 0 }));
                    Log("[模拟主机] 设备上线（S1F17→S1F18）");
                    break;

                case 5 when function == 1:
                    // S5F1 告警报告 → S5F2（确认）
                    ReplyWithBody(frame, 5, 2, SecsItem.CreateBinary(new byte[] { 0 }));
                    Log("[模拟主机] 告警确认（S5F1→S5F2）");
                    break;

                case 6 when function == 11:
                    // S6F11 事件报告 → S6F12（确认）
                    ReplyWithBody(frame, 6, 12, SecsItem.CreateBinary(new byte[] { 0 }));
                    Log("[模拟主机] 事件确认（S6F11→S6F12）");
                    break;

                default:
                    // 未处理的消息，发送通用确认
                    if (frame.Header.WBit)
                    {
                        byte replyFunc = (byte)(function + 1);
                        ReplyWithBody(frame, stream, replyFunc, null);
                    }
                    break;
            }
        }

        // ========== 主动发送 ==========

        /// <summary>
        /// 主动发送远程命令（S2F41）。
        /// </summary>
        public void SendRemoteCommand(string command, Dictionary<string, string>? parameters = null)
        {
            // <L [2]
            //   <A RCMD>
            //   <L [n]
            //     <L [2] <A CPNAME> <A CPVAL> >
            //   >
            // >
            var body = SecsItem.CreateList();
            body.Children.Add(SecsItem.CreateAscii(command));

            var paramList = SecsItem.CreateList();
            if (parameters != null)
            {
                foreach (var (name, value) in parameters)
                {
                    var pair = SecsItem.CreateList();
                    pair.Children.Add(SecsItem.CreateAscii(name));
                    pair.Children.Add(SecsItem.CreateAscii(value));
                    paramList.Children.Add(pair);
                }
            }
            body.Children.Add(paramList);

            byte[] bodyBytes = SecsIICodec.Encode(body);
            var frame = HsmsFrame.CreateDataMessage(_sessionId, 2, 41, true, bodyBytes);
            SendFrame(frame);

            Log($"[模拟主机] 发送远程命令：{command}");
        }

        /// <summary>
        /// 主动查询状态变量（S1F3）。
        /// </summary>
        public void QueryStatusVariables(List<uint> svIds)
        {
            var body = SecsItem.CreateList();
            foreach (var id in svIds)
                body.Children.Add(SecsItem.CreateU4(id));

            byte[] bodyBytes = SecsIICodec.Encode(body);
            var frame = HsmsFrame.CreateDataMessage(_sessionId, 1, 3, true, bodyBytes);
            SendFrame(frame);
        }

        // ========== 辅助 ==========

        private SecsItem BuildHostIdentity()
        {
            var list = SecsItem.CreateList();
            list.Children.Add(SecsItem.CreateAscii("HostSimulator"));
            list.Children.Add(SecsItem.CreateAscii("V1.0"));
            return list;
        }

        private void ReplyWithBody(HsmsFrame request, byte stream, byte function, SecsItem? body)
        {
            byte[] bodyBytes = body != null ? SecsIICodec.Encode(body) : Array.Empty<byte>();
            var reply = HsmsFrame.CreateDataMessage(
                _sessionId, stream, function, false, bodyBytes, request.Header.SystemBytes);
            SendFrame(reply);
        }

        private void SendFrame(HsmsFrame frame)
        {
            lock (_sendLock)
            {
                if (_stream == null || !IsDeviceConnected) return;
                byte[] wire = frame.ToWireFormat();
                _stream.Write(wire, 0, wire.Length);
                _stream.Flush();
            }
        }

        private bool ReadExact(byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = _stream!.Read(buffer, offset, count - offset);
                if (read == 0) return false;
                offset += read;
            }
            return true;
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
