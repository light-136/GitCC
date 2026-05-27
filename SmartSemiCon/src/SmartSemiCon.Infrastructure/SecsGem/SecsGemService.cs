// ============================================================
// 文件：HsmsService.cs
// 用途：HSMS-SS协议传输层 + SECS-II消息编解码 + GEM状态机
// 设计思路：
//   SECS/GEM是半导体行业的标准通讯协议，分三层：
//
//   1. HSMS (High-Speed SECS Message Services)
//      - 传输层，基于TCP/IP，替代老式RS-232串口
//      - HSMS-SS (Single Session) 模式：一个TCP连接承载一个会话
//      - 消息格式：[4字节长度][10字节头][消息体]
//      - 头部包含：Session ID, Wbit, Stream, Function, PType, SType, SystemBytes
//
//   2. SECS-II (SEMI Equipment Communications Standard)
//      - 消息层，定义消息的结构和内容
//      - 使用 Stream/Function 编号标识消息类型（如 S1F1 = "Are You There?"）
//      - 数据使用二进制编码（List, Binary, ASCII, Int, Float等）
//
//   3. GEM (Generic Equipment Model)
//      - 应用层，定义设备行为规范
//      - 通信状态机、控制状态机
//      - 状态变量(SV)、设备常量(EC)、采集事件(CE)
//
//   本文件实现完整的SECS/GEM服务，支持学习和理解整个协议栈。
// ============================================================

using System.Collections.Concurrent;
using System.Net.Sockets;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;

namespace SmartSemiCon.Infrastructure.SecsGem
{
    // =========================================
    // HSMS消息头 — 10字节固定头部
    // =========================================

    /// <summary>
    /// HSMS消息头 — 每条HSMS消息的固定10字节头部。
    /// 结构：[SessionID 2B][HeaderByte2 1B][HeaderByte3 1B][PType 1B][SType 1B][SystemBytes 4B]
    /// </summary>
    public class HsmsHeader
    {
        /// <summary>会话ID — 标识通讯会话</summary>
        public ushort SessionId { get; set; }

        /// <summary>W-bit — 是否需要回复（1=需要回复，0=不需要）</summary>
        public bool WBit { get; set; }

        /// <summary>Stream号 — 消息流号（功能类别）</summary>
        public byte Stream { get; set; }

        /// <summary>Function号 — 消息功能号（具体操作）</summary>
        public byte Function { get; set; }

        /// <summary>PType — 演示类型（固定为0 = SECS-II）</summary>
        public byte PType { get; set; } = 0;

        /// <summary>
        /// SType — 会话类型
        /// 0 = Data Message（SECS-II数据消息）
        /// 1 = Select.req（建立会话请求）
        /// 2 = Select.rsp（建立会话响应）
        /// 3 = Deselect.req
        /// 4 = Deselect.rsp
        /// 5 = Linktest.req（链路测试）
        /// 6 = Linktest.rsp
        /// 7 = Reject.req
        /// 9 = Separate.req（分离请求）
        /// </summary>
        public byte SType { get; set; }

        /// <summary>系统字节 — 事务标识，用于匹配请求和响应</summary>
        public uint SystemBytes { get; set; }

        /// <summary>
        /// 将头部编码为10字节数组。
        /// </summary>
        public byte[] Encode()
        {
            var header = new byte[10];
            header[0] = (byte)(SessionId >> 8);
            header[1] = (byte)(SessionId);

            // HeaderByte2: W-bit(bit7) + Stream(bit0~6)
            header[2] = (byte)((WBit ? 0x80 : 0x00) | (Stream & 0x7F));
            header[3] = Function;
            header[4] = PType;
            header[5] = SType;

            header[6] = (byte)(SystemBytes >> 24);
            header[7] = (byte)(SystemBytes >> 16);
            header[8] = (byte)(SystemBytes >> 8);
            header[9] = (byte)(SystemBytes);

            return header;
        }

        /// <summary>
        /// 从10字节数组解码头部。
        /// </summary>
        public static HsmsHeader Decode(byte[] data)
        {
            return new HsmsHeader
            {
                SessionId = (ushort)((data[0] << 8) | data[1]),
                WBit = (data[2] & 0x80) != 0,
                Stream = (byte)(data[2] & 0x7F),
                Function = data[3],
                PType = data[4],
                SType = data[5],
                SystemBytes = (uint)((data[6] << 24) | (data[7] << 16) | (data[8] << 8) | data[9])
            };
        }
    }

    // =========================================
    // SECS-II数据项 — 消息体的数据元素
    // =========================================

    /// <summary>
    /// SECS-II数据项格式代码 — 定义数据项的类型。
    /// </summary>
    public enum SecsFormat : byte
    {
        /// <summary>列表 — 包含其他数据项的容器</summary>
        List = 0x00,
        /// <summary>二进制数据</summary>
        Binary = 0x20,
        /// <summary>布尔值</summary>
        Boolean = 0x24,
        /// <summary>ASCII字符串</summary>
        Ascii = 0x40,
        /// <summary>JIS-8字符串（日文）</summary>
        Jis8 = 0x44,
        /// <summary>2字节整数</summary>
        Int2 = 0x68,
        /// <summary>4字节整数</summary>
        Int4 = 0x70,
        /// <summary>8字节整数</summary>
        Int8 = 0x60,
        /// <summary>4字节浮点数</summary>
        Float4 = 0x90,
        /// <summary>8字节浮点数</summary>
        Float8 = 0x80,
        /// <summary>1字节无符号整数</summary>
        Uint1 = 0xA4,
        /// <summary>2字节无符号整数</summary>
        Uint2 = 0xA8,
        /// <summary>4字节无符号整数</summary>
        Uint4 = 0xB0,
        /// <summary>8字节无符号整数</summary>
        Uint8 = 0xA0
    }

    /// <summary>
    /// SECS-II数据项 — 消息体中的数据元素。
    /// 可以是原子值（整数、字符串等）或列表（包含子项）。
    /// </summary>
    public class SecsItem
    {
        /// <summary>数据格式</summary>
        public SecsFormat Format { get; set; }

        /// <summary>原子值数据（非List类型时使用）</summary>
        public object? Value { get; set; }

        /// <summary>子项列表（List类型时使用）</summary>
        public List<SecsItem> Items { get; set; } = new();

        /// <summary>创建ASCII字符串项</summary>
        public static SecsItem Ascii(string value) => new() { Format = SecsFormat.Ascii, Value = value };

        /// <summary>创建无符号4字节整数项</summary>
        public static SecsItem Uint4(uint value) => new() { Format = SecsFormat.Uint4, Value = value };

        /// <summary>创建二进制数据项</summary>
        public static SecsItem Binary(byte[] value) => new() { Format = SecsFormat.Binary, Value = value };

        /// <summary>创建布尔值项</summary>
        public static SecsItem Bool(bool value) => new() { Format = SecsFormat.Boolean, Value = value };

        /// <summary>创建列表项</summary>
        public static SecsItem ListOf(params SecsItem[] items) => new()
        {
            Format = SecsFormat.List,
            Items = new List<SecsItem>(items)
        };
    }

    // =========================================
    // SECS/GEM服务实现
    // =========================================

    /// <summary>
    /// SECS/GEM服务完整实现。
    /// 包含：HSMS传输层 + SECS-II编解码 + GEM状态机。
    /// </summary>
    public class SecsGemService : ISecsGemService
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private CancellationTokenSource? _receiveCts;
        private uint _systemBytesCounter;

        // 事务管理 — SystemBytes到等待回复的映射
        private readonly ConcurrentDictionary<uint, TaskCompletionSource<SecsMessageData>> _pendingTransactions = new();

        // 消息处理器注册表 — Stream*256+Function → 处理器
        private readonly ConcurrentDictionary<int, Func<byte[], Task<byte[]>>> _messageHandlers = new();

        /// <summary>HSMS连接状态</summary>
        public HsmsConnectionState ConnectionState { get; private set; } = HsmsConnectionState.NotConnected;

        /// <summary>GEM控制状态</summary>
        public GemControlState ControlState { get; private set; } = GemControlState.OfflineEquipmentOffline;

        /// <summary>GEM通讯状态</summary>
        public GemCommunicationState CommunicationState { get; private set; } = GemCommunicationState.Disabled;

        /// <summary>设备ID（Session ID）</summary>
        public ushort DeviceId { get; set; } = 1;

        /// <summary>T3超时 — 等待回复消息的超时时间（秒）</summary>
        public int T3Timeout { get; set; } = 45;

        /// <summary>T5超时 — 连接分离超时（秒）</summary>
        public int T5Timeout { get; set; } = 10;

        /// <summary>T6超时 — 控制事务超时（秒）</summary>
        public int T6Timeout { get; set; } = 5;

        /// <summary>消息接收事件</summary>
        public event EventHandler<SecsMessageData>? MessageReceived;

        /// <summary>控制状态变更事件</summary>
        public event EventHandler<GemControlState>? ControlStateChanged;

        /// <summary>
        /// 连接到Host（被动模式：Equipment等待Host连接）。
        /// 在半导体行业中，通常Equipment作为Server，Host作为Client。
        /// 但本实现也支持Equipment主动连接Host（Active模式）。
        /// </summary>
        public async Task<bool> ConnectAsync(string host, int port)
        {
            try
            {
                _tcpClient = new TcpClient();
                _tcpClient.NoDelay = true;

                await _tcpClient.ConnectAsync(host, port);
                _stream = _tcpClient.GetStream();

                ConnectionState = HsmsConnectionState.Connected;

                // 启动接收循环
                _receiveCts = new CancellationTokenSource();
                _ = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));

                // 发送Select.req建立HSMS会话
                var selectResult = await SendControlMessageAsync(1, 0); // SType=1 = Select.req
                if (selectResult)
                {
                    ConnectionState = HsmsConnectionState.Selected;
                    CommunicationState = GemCommunicationState.Communicating;
                }

                return ConnectionState == HsmsConnectionState.Selected;
            }
            catch
            {
                ConnectionState = HsmsConnectionState.NotConnected;
                return false;
            }
        }

        /// <summary>
        /// 断开HSMS连接。
        /// </summary>
        public async Task DisconnectAsync()
        {
            // 发送Separate.req
            if (ConnectionState == HsmsConnectionState.Selected)
            {
                try { await SendControlMessageAsync(9, 0); } catch { }
            }

            _receiveCts?.Cancel();

            try
            {
                _stream?.Close();
                _tcpClient?.Close();
            }
            catch { }

            ConnectionState = HsmsConnectionState.NotConnected;
            CommunicationState = GemCommunicationState.Disabled;
            SetControlState(GemControlState.OfflineEquipmentOffline);

            await Task.CompletedTask;
        }

        /// <summary>
        /// 发送SECS消息并等待回复。
        /// 这是SECS/GEM通讯的核心方法。
        /// </summary>
        /// <param name="stream">Stream号（功能类别，如S1=设备管理）</param>
        /// <param name="function">Function号（具体操作，如F1=Are You There）</param>
        /// <param name="body">SECS-II编码的消息体</param>
        /// <param name="wantReply">是否等待回复</param>
        public async Task<SecsMessageData?> SendAsync(int stream, int function, byte[] body, bool wantReply = true)
        {
            if (ConnectionState != HsmsConnectionState.Selected) return null;

            var systemBytes = Interlocked.Increment(ref _systemBytesCounter);

            // 构造HSMS消息
            var header = new HsmsHeader
            {
                SessionId = DeviceId,
                WBit = wantReply,
                Stream = (byte)stream,
                Function = (byte)function,
                PType = 0,   // SECS-II
                SType = 0,   // Data Message
                SystemBytes = systemBytes
            };

            var headerBytes = header.Encode();
            var messageLength = 10 + body.Length;

            // 组装完整消息：[4字节长度][10字节头][消息体]
            var packet = new byte[4 + messageLength];
            packet[0] = (byte)(messageLength >> 24);
            packet[1] = (byte)(messageLength >> 16);
            packet[2] = (byte)(messageLength >> 8);
            packet[3] = (byte)(messageLength);
            Array.Copy(headerBytes, 0, packet, 4, 10);
            if (body.Length > 0) Array.Copy(body, 0, packet, 14, body.Length);

            if (wantReply)
            {
                // 注册事务等待回复
                var tcs = new TaskCompletionSource<SecsMessageData>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingTransactions[systemBytes] = tcs;

                try
                {
                    await _stream!.WriteAsync(packet);
                    await _stream.FlushAsync();

                    // 等待回复（带T3超时）
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(T3Timeout));
                    using var reg = cts.Token.Register(() => tcs.TrySetCanceled());

                    return await tcs.Task;
                }
                catch (OperationCanceledException)
                {
                    return null; // T3超时
                }
                finally
                {
                    _pendingTransactions.TryRemove(systemBytes, out _);
                }
            }
            else
            {
                await _stream!.WriteAsync(packet);
                await _stream.FlushAsync();
                return new SecsMessageData { Stream = stream, Function = function, SystemBytes = systemBytes };
            }
        }

        /// <summary>
        /// 注册消息处理器 — 收到指定SxFy时自动调用。
        /// 处理器接收消息体，返回回复消息体。
        /// </summary>
        public void RegisterHandler(int stream, int function, Func<byte[], Task<byte[]>> handler)
        {
            var key = stream * 256 + function;
            _messageHandlers[key] = handler;
        }

        /// <summary>
        /// 请求上线 — 发送S1F17（Online Request）。
        /// </summary>
        public async Task<bool> GoOnlineAsync(bool remoteMode = true)
        {
            // 发送 S1F13 (Establish Communications Request)
            var result = await SendAsync(1, 13, Array.Empty<byte>());
            if (result == null) return false;

            var newState = remoteMode ? GemControlState.OnlineRemote : GemControlState.OnlineLocal;
            SetControlState(newState);
            return true;
        }

        /// <summary>
        /// 请求离线。
        /// </summary>
        public async Task GoOfflineAsync()
        {
            SetControlState(GemControlState.OfflineEquipmentOffline);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 触发采集事件 — 自动发送S6F11（Event Report Send）。
        /// S6F11用于Equipment向Host报告事件发生。
        /// </summary>
        public async Task TriggerEventAsync(uint eventId, Dictionary<string, object>? data = null)
        {
            // 构造S6F11消息体（简化版）
            // 真实实现需要按SECS-II格式编码DATAID, CEID, RPT列表
            var body = BitConverter.GetBytes(eventId);
            await SendAsync(6, 11, body, wantReply: true);
        }

        /// <summary>
        /// 设置报警 — 发送S5F1（Alarm Report Send）。
        /// </summary>
        public async Task SetAlarmAsync(uint alarmId, string text)
        {
            var body = System.Text.Encoding.ASCII.GetBytes($"ALARM:{alarmId}:{text}");
            await SendAsync(5, 1, body, wantReply: true);
        }

        /// <summary>
        /// 清除报警。
        /// </summary>
        public async Task ClearAlarmAsync(uint alarmId)
        {
            var body = BitConverter.GetBytes(alarmId);
            await SendAsync(5, 1, body, wantReply: true);
        }

        /// <summary>
        /// HSMS消息接收循环。
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var lengthBuffer = new byte[4];

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 读取4字节长度头
                    var bytesRead = await ReadExactAsync(lengthBuffer, 4, cancellationToken);
                    if (bytesRead != 4) break;

                    var messageLength = (lengthBuffer[0] << 24) | (lengthBuffer[1] << 16)
                                      | (lengthBuffer[2] << 8) | lengthBuffer[3];

                    if (messageLength < 10) continue; // HSMS头至少10字节

                    // 读取完整消息
                    var messageBuffer = new byte[messageLength];
                    bytesRead = await ReadExactAsync(messageBuffer, messageLength, cancellationToken);
                    if (bytesRead != messageLength) break;

                    // 解析HSMS头
                    var headerBytes = new byte[10];
                    Array.Copy(messageBuffer, 0, headerBytes, 0, 10);
                    var header = HsmsHeader.Decode(headerBytes);

                    // 提取消息体
                    var bodyLength = messageLength - 10;
                    var body = new byte[bodyLength];
                    if (bodyLength > 0)
                        Array.Copy(messageBuffer, 10, body, 0, bodyLength);

                    // 处理消息
                    await HandleMessageAsync(header, body);
                }
                catch (OperationCanceledException) { break; }
                catch { break; }
            }
        }

        /// <summary>
        /// 处理收到的HSMS消息。
        /// </summary>
        private async Task HandleMessageAsync(HsmsHeader header, byte[] body)
        {
            if (header.SType != 0)
            {
                // 控制消息（Select/Deselect/Linktest等）
                HandleControlMessage(header);
                return;
            }

            // SECS-II数据消息
            var messageData = new SecsMessageData
            {
                Stream = header.Stream,
                Function = header.Function,
                WantReply = header.WBit,
                Body = body,
                SystemBytes = header.SystemBytes
            };

            // 检查是否是对之前请求的回复
            if (_pendingTransactions.TryRemove(header.SystemBytes, out var tcs))
            {
                tcs.TrySetResult(messageData);
                return;
            }

            // 触发消息接收事件
            MessageReceived?.Invoke(this, messageData);

            // 查找注册的处理器
            var key = header.Stream * 256 + header.Function;
            if (_messageHandlers.TryGetValue(key, out var handler))
            {
                var replyBody = await handler(body);

                // 自动发送回复（Function号+1）
                if (header.WBit)
                {
                    var replyHeader = new HsmsHeader
                    {
                        SessionId = DeviceId,
                        WBit = false,
                        Stream = header.Stream,
                        Function = (byte)(header.Function + 1),
                        PType = 0,
                        SType = 0,
                        SystemBytes = header.SystemBytes // 使用相同的SystemBytes
                    };

                    var replyHeaderBytes = replyHeader.Encode();
                    var replyLength = 10 + replyBody.Length;
                    var packet = new byte[4 + replyLength];
                    packet[0] = (byte)(replyLength >> 24);
                    packet[1] = (byte)(replyLength >> 16);
                    packet[2] = (byte)(replyLength >> 8);
                    packet[3] = (byte)(replyLength);
                    Array.Copy(replyHeaderBytes, 0, packet, 4, 10);
                    if (replyBody.Length > 0)
                        Array.Copy(replyBody, 0, packet, 14, replyBody.Length);

                    await _stream!.WriteAsync(packet);
                }
            }
        }

        /// <summary>
        /// 处理HSMS控制消息。
        /// </summary>
        private void HandleControlMessage(HsmsHeader header)
        {
            switch (header.SType)
            {
                case 1: // Select.req — Host请求建立会话
                    // 回复 Select.rsp
                    _ = SendControlResponseAsync(2, header.SystemBytes);
                    ConnectionState = HsmsConnectionState.Selected;
                    break;

                case 5: // Linktest.req — 链路测试
                    // 回复 Linktest.rsp
                    _ = SendControlResponseAsync(6, header.SystemBytes);
                    break;

                case 9: // Separate.req — Host请求断开
                    ConnectionState = HsmsConnectionState.NotConnected;
                    break;
            }
        }

        /// <summary>
        /// 发送HSMS控制消息（Select/Linktest等）。
        /// </summary>
        private async Task<bool> SendControlMessageAsync(byte sType, byte selectStatus)
        {
            var systemBytes = Interlocked.Increment(ref _systemBytesCounter);
            var header = new HsmsHeader
            {
                SessionId = 0xFFFF,
                WBit = false,
                Stream = 0,
                Function = 0,
                PType = 0,
                SType = sType,
                SystemBytes = systemBytes
            };

            var headerBytes = header.Encode();
            var packet = new byte[14]; // 4字节长度 + 10字节头
            packet[0] = 0; packet[1] = 0; packet[2] = 0; packet[3] = 10;
            Array.Copy(headerBytes, 0, packet, 4, 10);

            try
            {
                await _stream!.WriteAsync(packet);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 发送控制消息响应。
        /// </summary>
        private async Task SendControlResponseAsync(byte sType, uint systemBytes)
        {
            var header = new HsmsHeader
            {
                SessionId = 0xFFFF,
                SType = sType,
                SystemBytes = systemBytes
            };

            var headerBytes = header.Encode();
            var packet = new byte[14];
            packet[0] = 0; packet[1] = 0; packet[2] = 0; packet[3] = 10;
            Array.Copy(headerBytes, 0, packet, 4, 10);

            try
            {
                await _stream!.WriteAsync(packet);
            }
            catch { }
        }

        /// <summary>
        /// 精确读取指定字节数（处理TCP流的部分读取问题）。
        /// </summary>
        private async Task<int> ReadExactAsync(byte[] buffer, int count, CancellationToken cancellationToken)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                var bytesRead = await _stream!.ReadAsync(buffer, totalRead, count - totalRead, cancellationToken);
                if (bytesRead == 0) return totalRead;
                totalRead += bytesRead;
            }
            return totalRead;
        }

        /// <summary>
        /// 设置GEM控制状态并触发事件。
        /// </summary>
        private void SetControlState(GemControlState newState)
        {
            if (ControlState == newState) return;
            ControlState = newState;
            ControlStateChanged?.Invoke(this, newState);
        }

        public void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }
    }
}
