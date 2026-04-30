// ============================================================
// 文件：SecsGemService.cs
// 用途：SECS/GEM 顶层编排服务 — 组合所有子模块，提供统一接口
// 标准：SEMI E5/E30/E37
// 设计思路：
//   SecsGemService 是 SECS/GEM 系统的门面（Facade），将以下子模块
//   统一编排为一个完整的 GEM 合规服务：
//   - HsmsConnection（传输层）
//   - GemStateMachine（状态机）
//   - GemDataManager（数据管理）
//   - GemAlarmManager（告警管理）
//   - GemRemoteCommandHandler（远程命令）
//   - GemProcessProgramManager（工艺程序）
//   - SecsIICodec（编解码）
//
//   核心流程：
//   连接 → Select → S1F13建立通信 → S1F17请求上线 → 在线运行
//   在线后可以：事件上报、告警上报、处理远程命令、管理工艺程序
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Services.SecsGem
{
    /// <summary>
    /// 消息日志条目 — 记录收发的 SECS 消息。
    /// </summary>
    public class SecsMessageLogEntry
    {
        /// <summary>时间戳。</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>方向（发送/接收）。</summary>
        public string Direction { get; set; } = "";

        /// <summary>消息标识（如 S1F13）。</summary>
        public string MessageId { get; set; } = "";

        /// <summary>SML 文本。</summary>
        public string SmlText { get; set; } = "";
    }

    /// <summary>
    /// SECS/GEM 顶层编排服务 — 组合所有 GEM 子模块，提供完整的半导体通信功能。
    ///
    /// 使用示例：
    ///   var service = new SecsGemService("192.168.1.100", 5000);
    ///   service.RegisterStatusVariable(new StatusVariable { SvId = 1, Name = "温度" });
    ///   await service.ConnectAsync();
    ///   await service.SelectAsync();
    ///   await service.EnableAsync();
    ///   await service.GoOnlineAsync();
    ///   // ... 运行中 ...
    ///   service.Disconnect();
    /// </summary>
    public class SecsGemService : IDisposable
    {
        // ========== 子模块 ==========

        /// <summary>HSMS 传输层。</summary>
        public HsmsConnection Connection { get; }

        /// <summary>GEM 状态机。</summary>
        public GemStateMachine StateMachine { get; }

        /// <summary>数据管理器。</summary>
        public GemDataManager DataManager { get; }

        /// <summary>告警管理器。</summary>
        public GemAlarmManager AlarmManager { get; }

        /// <summary>远程命令处理器。</summary>
        public GemRemoteCommandHandler RemoteCommandHandler { get; }

        /// <summary>工艺程序管理器。</summary>
        public GemProcessProgramManager ProcessProgramManager { get; }

        // 消息日志
        private readonly List<SecsMessageLogEntry> _messageLog = new();
        private readonly object _logLock = new();

        // 自定义消息处理器
        private readonly Dictionary<string, Action<SecsMessage>> _messageHandlers = new();

        // 心跳定时器
        private CancellationTokenSource? _heartbeatCts;

        // 配置
        private readonly ushort _sessionId;

        /// <summary>设备名称。</summary>
        public string EquipmentName { get; set; } = "SmartMES";

        /// <summary>软件版本。</summary>
        public string SoftwareVersion { get; set; } = "V9.0";

        // ========== 事件 ==========

        /// <summary>消息接收事件。</summary>
        public event EventHandler<SecsMessage>? MessageReceived;

        /// <summary>消息发送事件。</summary>
        public event EventHandler<SecsMessage>? MessageSent;

        /// <summary>控制状态变更事件。</summary>
        public event EventHandler<GemControlState>? ControlStateChanged;

        /// <summary>通信状态变更事件。</summary>
        public event EventHandler<GemCommunicationState>? CommunicationStateChanged;

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 构造函数。
        /// </summary>
        public SecsGemService(string host, int port, ushort sessionId = 0,
                               HsmsTimerConfig? timerConfig = null)
        {
            _sessionId = sessionId;

            // 初始化子模块
            Connection = new HsmsConnection(host, port, sessionId, timerConfig);
            StateMachine = new GemStateMachine();
            DataManager = new GemDataManager();
            AlarmManager = new GemAlarmManager();
            RemoteCommandHandler = new GemRemoteCommandHandler();
            ProcessProgramManager = new GemProcessProgramManager();

            // 订阅子模块事件
            Connection.MessageReceived += OnHsmsMessageReceived;
            Connection.MessageLogged += (_, msg) => Log(msg);
            Connection.Disconnected += (_, _) => StateMachine.CommunicationLost();

            StateMachine.CommunicationStateChanged += (_, state) =>
                CommunicationStateChanged?.Invoke(this, state);
            StateMachine.ControlStateChanged += (_, state) =>
                ControlStateChanged?.Invoke(this, state);
            StateMachine.MessageLogged += (_, msg) => Log(msg);

            DataManager.MessageLogged += (_, msg) => Log(msg);
            AlarmManager.MessageLogged += (_, msg) => Log(msg);
            RemoteCommandHandler.MessageLogged += (_, msg) => Log(msg);
            ProcessProgramManager.MessageLogged += (_, msg) => Log(msg);
        }

        // ========== 连接与握手 ==========

        /// <summary>
        /// 建立 TCP 连接。
        /// </summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            await Connection.ConnectAsync(ct);
        }

        /// <summary>
        /// HSMS Select 握手。
        /// </summary>
        public async Task<bool> SelectAsync(CancellationToken ct = default)
        {
            return await Connection.SelectAsync(ct);
        }

        /// <summary>
        /// 启用通信 — 发送 S1F13 建立通信。
        /// </summary>
        public async Task<bool> EnableAsync(CancellationToken ct = default)
        {
            StateMachine.Enable();

            // 构建 S1F13 消息体
            // <L [3]
            //   <A MDLN>      ; 设备型号
            //   <A SOFTREV>   ; 软件版本
            //   <U4 1>        ; 设备 ID
            // >
            var body = SecsItem.CreateList();
            body.Children.Add(SecsItem.CreateAscii(EquipmentName));
            body.Children.Add(SecsItem.CreateAscii(SoftwareVersion));

            var msg = new SecsMessage
            {
                Stream = 1, Function = 13, WBit = true,
                Body = body
            };

            var reply = await SendAsync(msg, ct);

            if (reply != null)
            {
                StateMachine.CommunicationEstablished();
                Log("[GEM] 通信建立成功");
                return true;
            }

            Log("[GEM] 通信建立失败");
            return false;
        }

        /// <summary>
        /// 请求上线 — 发送 S1F17。
        /// </summary>
        public async Task<bool> GoOnlineAsync(CancellationToken ct = default)
        {
            if (!StateMachine.RequestOnline())
                return false;

            var msg = new SecsMessage
            {
                Stream = 1, Function = 17, WBit = true,
                Body = null
            };

            var reply = await SendAsync(msg, ct);

            if (reply?.Body != null)
            {
                // S1F18 回复中的 ONLACK：0=接受
                var ack = reply.Body.Value;
                if (ack != null && Convert.ToInt32(ack) == 0)
                {
                    StateMachine.OnlineAccepted();

                    // 启动心跳
                    StartHeartbeat();

                    Log("[GEM] 上线成功 — OnlineRemote");
                    return true;
                }
            }

            StateMachine.OnlineRejected();
            Log("[GEM] 上线被拒绝");
            return false;
        }

        /// <summary>
        /// 断开连接。
        /// </summary>
        public void Disconnect()
        {
            StopHeartbeat();
            Connection.Disconnect();
            StateMachine.Disable();
        }

        // ========== 消息收发 ==========

        /// <summary>
        /// 发送 SECS 消息并等待回复。
        /// </summary>
        public async Task<SecsMessage?> SendAsync(SecsMessage msg, CancellationToken ct = default)
        {
            byte[] body = msg.Body != null ? SecsIICodec.Encode(msg.Body) : Array.Empty<byte>();

            var frame = HsmsFrame.CreateDataMessage(
                _sessionId, (byte)msg.Stream, (byte)msg.Function, msg.WBit, body);

            msg.SystemBytes = frame.Header.SystemBytes;

            // 记录发送日志
            LogMessage("→ 发送", msg);
            MessageSent?.Invoke(this, msg);

            if (!msg.WBit)
            {
                Connection.SendFrame(frame);
                return null;
            }

            var reply = await Connection.SendAndWaitAsync(frame, 45000, ct);
            if (reply == null) return null;

            var replyMsg = new SecsMessage
            {
                Stream = reply.Header.Stream,
                Function = reply.Header.Function,
                WBit = reply.Header.WBit,
                SystemBytes = reply.Header.SystemBytes
            };

            if (reply.Body.Length > 0)
            {
                replyMsg.Body = SecsIICodec.Decode(reply.Body);
            }

            LogMessage("← 接收", replyMsg);
            MessageReceived?.Invoke(this, replyMsg);
            return replyMsg;
        }

        /// <summary>
        /// 注册自定义消息处理器。
        /// </summary>
        public void RegisterMessageHandler(byte stream, byte function,
                                             Action<SecsMessage> handler)
        {
            string key = $"S{stream}F{function}";
            _messageHandlers[key] = handler;
        }

        // ========== 数据管理快捷方法 ==========

        /// <summary>注册状态变量。</summary>
        public void RegisterStatusVariable(StatusVariable sv)
            => DataManager.RegisterStatusVariable(sv);

        /// <summary>注册设备常量。</summary>
        public void RegisterEquipmentConstant(EquipmentConstant ec)
            => DataManager.RegisterEquipmentConstant(ec);

        /// <summary>注册告警。</summary>
        public void RegisterAlarm(SecsAlarm alarm)
            => AlarmManager.RegisterAlarm(alarm);

        /// <summary>注册远程命令。</summary>
        public void RegisterRemoteCommand(string name, string description,
                                            List<string>? parameters,
                                            Func<Dictionary<string, string>, RemoteCommandResult> handler)
            => RemoteCommandHandler.RegisterCommand(name, description, parameters, handler);

        // ========== 事件上报 ==========

        /// <summary>
        /// 触发采集事件 — 构建并发送 S6F11。
        /// </summary>
        public async Task TriggerCollectionEventAsync(uint ceId, CancellationToken ct = default)
        {
            var body = DataManager.BuildEventReport(ceId);
            if (body == null)
            {
                Log($"[GEM] 事件 {ceId} 未启用或不存在");
                return;
            }

            var msg = new SecsMessage
            {
                Stream = 6, Function = 11, WBit = true,
                Body = body
            };

            await SendAsync(msg, ct);
            Log($"[GEM] 事件 {ceId} 已上报");
        }

        /// <summary>
        /// 设置告警并上报 S5F1。
        /// </summary>
        public async Task SetAlarmAsync(uint alarmId, bool isSet, CancellationToken ct = default)
        {
            if (isSet)
                AlarmManager.SetAlarm(alarmId);
            else
                AlarmManager.ClearAlarm(alarmId);

            var body = AlarmManager.BuildAlarmReport(alarmId, isSet);
            if (body != null)
            {
                var msg = new SecsMessage
                {
                    Stream = 5, Function = 1, WBit = true,
                    Body = body
                };
                await SendAsync(msg, ct);
            }
        }

        // ========== 工艺程序管理 ==========

        /// <summary>获取工艺程序列表。</summary>
        public List<string> GetProcessProgramList()
            => ProcessProgramManager.GetProgramList();

        // ========== 消息日志 ==========

        /// <summary>获取消息日志。</summary>
        public List<SecsMessageLogEntry> GetMessageLog()
        {
            lock (_logLock) { return _messageLog.ToList(); }
        }

        // ========== 接收消息处理 ==========

        /// <summary>
        /// 处理从 HSMS 层收到的数据消息。
        /// 根据 Stream/Function 分发给对应的处理器。
        /// </summary>
        private void OnHsmsMessageReceived(object? sender, HsmsFrame frame)
        {
            if (frame.Header.SType != HsmsMessageType.DataMessage) return;

            var msg = new SecsMessage
            {
                Stream = frame.Header.Stream,
                Function = frame.Header.Function,
                WBit = frame.Header.WBit,
                SystemBytes = frame.Header.SystemBytes
            };

            if (frame.Body.Length > 0)
                msg.Body = SecsIICodec.Decode(frame.Body);

            LogMessage("← 接收", msg);

            // 分发给注册的处理器
            string key = $"S{msg.Stream}F{msg.Function}";
            if (_messageHandlers.TryGetValue(key, out var handler))
            {
                handler(msg);
                return;
            }

            // 默认处理
            HandleDefaultMessage(msg, frame);
        }

        /// <summary>
        /// 默认消息处理 — 处理 GEM 标准消息。
        /// </summary>
        private void HandleDefaultMessage(SecsMessage msg, HsmsFrame frame)
        {
            switch (msg.Stream)
            {
                case 1 when msg.Function == 1:
                    // S1F1 心跳请求 → 回复 S1F2
                    var s1f2Body = SecsItem.CreateList();
                    s1f2Body.Children.Add(SecsItem.CreateAscii(EquipmentName));
                    s1f2Body.Children.Add(SecsItem.CreateAscii(SoftwareVersion));
                    SendReply(frame, 1, 2, s1f2Body);
                    break;

                case 1 when msg.Function == 3:
                    // S1F3 查询SV → 回复 S1F4
                    var svIds = ExtractUintList(msg.Body);
                    var s1f4Body = DataManager.BuildSvResponse(svIds);
                    SendReply(frame, 1, 4, s1f4Body);
                    break;

                case 1 when msg.Function == 15:
                    // S1F15 请求离线 → 回复 S1F16
                    StateMachine.GoOffline();
                    SendReply(frame, 1, 16, SecsItem.CreateBinary(new byte[] { 0 }));
                    break;

                case 2 when msg.Function == 41:
                    // S2F41 远程命令 → 回复 S2F42
                    if (msg.Body != null)
                    {
                        var result = RemoteCommandHandler.HandleS2F41(msg.Body);
                        SendReply(frame, 2, 42, result);
                    }
                    break;

                case 5 when msg.Function == 5:
                    // S5F5 列出所有告警 → 回复 S5F6
                    var alarmList = AlarmManager.BuildAlarmListResponse();
                    SendReply(frame, 5, 6, alarmList);
                    break;

                case 7 when msg.Function == 1:
                    // S7F1 工艺程序加载请求 → 回复 S7F2
                    if (msg.Body?.Children?.Count >= 2)
                    {
                        string ppId = msg.Body.Children[0].Value?.ToString() ?? "";
                        int ppLen = Convert.ToInt32(msg.Body.Children[1].Value ?? 0);
                        byte ppGnt = ProcessProgramManager.CanAcceptProgram(ppId, ppLen);
                        SendReply(frame, 7, 2, SecsItem.CreateBinary(new[] { ppGnt }));
                    }
                    else
                    {
                        SendReply(frame, 7, 2, SecsItem.CreateBinary(new byte[] { 3 }));
                    }
                    break;

                case 7 when msg.Function == 3:
                    // S7F3 工艺程序发送 → 回复 S7F4
                    if (msg.Body?.Children?.Count >= 2)
                    {
                        string ppId = msg.Body.Children[0].Value?.ToString() ?? "";
                        byte[] ppBody = msg.Body.Children[1].Value as byte[] ?? Array.Empty<byte>();
                        byte ackc7 = ProcessProgramManager.StoreProgram(ppId, ppBody);
                        SendReply(frame, 7, 4, SecsItem.CreateBinary(new[] { ackc7 }));
                    }
                    else
                    {
                        SendReply(frame, 7, 4, SecsItem.CreateBinary(new byte[] { 1 }));
                    }
                    break;

                case 7 when msg.Function == 5:
                    // S7F5 工艺程序请求 → 回复 S7F6
                    if (msg.Body != null)
                    {
                        string ppId = msg.Body.Value?.ToString() ?? "";
                        var ppResponse = ProcessProgramManager.BuildPPResponse(ppId);
                        SendReply(frame, 7, 6, ppResponse);
                    }
                    break;

                case 7 when msg.Function == 17:
                    // S7F17 工艺程序删除 → 回复 S7F18
                    if (msg.Body != null)
                    {
                        var ppIds = new List<string>();
                        if (msg.Body.Children != null)
                            foreach (var child in msg.Body.Children)
                                ppIds.Add(child.Value?.ToString() ?? "");
                        byte delAck = ProcessProgramManager.DeletePrograms(ppIds);
                        SendReply(frame, 7, 18, SecsItem.CreateBinary(new[] { delAck }));
                    }
                    break;

                case 7 when msg.Function == 19:
                    // S7F19 PP目录 → 回复 S7F20
                    var ppList = ProcessProgramManager.BuildProgramListResponse();
                    SendReply(frame, 7, 20, ppList);
                    break;
            }
        }

        /// <summary>
        /// 发送回复消息。
        /// </summary>
        private void SendReply(HsmsFrame request, byte stream, byte function, SecsItem? body)
        {
            byte[] bodyBytes = body != null ? SecsIICodec.Encode(body) : Array.Empty<byte>();
            Connection.SendDataMessage(stream, function, false, bodyBytes, request.Header.SystemBytes);
        }

        /// <summary>
        /// 从 SecsItem 中提取 uint 列表。
        /// </summary>
        private List<uint> ExtractUintList(SecsItem? item)
        {
            var list = new List<uint>();
            if (item == null) return list;

            if (item.Type == SecsItemType.List)
            {
                foreach (var child in item.Children)
                {
                    if (child.Value != null)
                        list.Add(Convert.ToUInt32(child.Value));
                }
            }
            return list;
        }

        // ========== 心跳 ==========

        private void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatCts = new CancellationTokenSource();
            _ = HeartbeatLoopAsync(_heartbeatCts.Token);
        }

        private void StopHeartbeat()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10000, ct); // 每10秒
                    if (ct.IsCancellationRequested) break;

                    // 发送 S1F1 心跳
                    var msg = new SecsMessage { Stream = 1, Function = 1, WBit = true };
                    var reply = await SendAsync(msg, ct);
                    if (reply == null)
                    {
                        Log("[GEM] 心跳无响应");
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log($"[GEM] 心跳异常：{ex.Message}");
                    break;
                }
            }
        }

        // ========== 日志 ==========

        private void LogMessage(string direction, SecsMessage msg)
        {
            string sml = msg.Body != null ? SecsIICodec.MessageToSml(msg) : $"S{msg.Stream}F{msg.Function}";

            lock (_logLock)
            {
                _messageLog.Add(new SecsMessageLogEntry
                {
                    Direction = direction,
                    MessageId = $"S{msg.Stream}F{msg.Function}",
                    SmlText = sml
                });

                // 限制日志条数
                if (_messageLog.Count > 1000)
                    _messageLog.RemoveRange(0, 500);
            }
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);

        public void Dispose()
        {
            StopHeartbeat();
            Connection.Dispose();
        }
    }
}
