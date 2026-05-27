// ============================================================
// 文件：SecsGemService.cs
// 层次：应用层 (Application Layer) — SECS/GEM 通信服务
// 职责：
//   提供半导体设备通信协议 SECS/GEM（SEMI E5/E30/E37）的应用层封装。
//   管理 GEM 状态模型（Communication / Control / Processing）、
//   SECS 消息收发、设备变量（SV/DVVAL）、集合事件（CEID）、报告（RPTID）。
// 设计思路：
//   SECS/GEM 是半导体行业的标准通信协议。
//   本服务在 Infrastructure 层 TCP 通信之上构建 SECS 消息语义层。
//   GEM 状态机遵循 SEMI E30 标准的三层状态模型。
// ============================================================

using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.Interfaces;
using System.Collections.Concurrent;

namespace SmartIndustry.Application.SecsGem
{
    /// <summary>GEM 通信状态（SEMI E30）</summary>
    public enum GemCommunicationState { Disabled, Enabled, WaitCRA, WaitDelay, WaitCRFromHost }

    /// <summary>GEM 控制状态（SEMI E30）</summary>
    public enum GemControlState { EquipmentOffline, AttemptOnline, HostOffline, OnlineLocal, OnlineRemote }

    /// <summary>GEM 处理状态</summary>
    public enum GemProcessingState { Idle, Setup, Ready, Executing, Paused }

    /// <summary>
    /// SECS 消息定义
    /// </summary>
    public class SecsMessage
    {
        public int Stream { get; init; }
        public int Function { get; init; }
        public bool WBit { get; init; }
        public string? Body { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;

        public string MessageId => $"S{Stream}F{Function}";
    }

    /// <summary>
    /// 设备变量（Status Variable / Data Variable）
    /// </summary>
    public class DeviceVariable
    {
        public int VariableId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public object? Value { get; set; }
        public Type ValueType { get; init; } = typeof(string);
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// SECS/GEM 通信服务
    /// </summary>
    public class SecsGemService : IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly ILogService _logService;

        // GEM 三层状态
        private GemCommunicationState _communicationState = GemCommunicationState.Disabled;
        private GemControlState _controlState = GemControlState.EquipmentOffline;
        private GemProcessingState _processingState = GemProcessingState.Idle;

        // 设备变量
        private readonly ConcurrentDictionary<int, DeviceVariable> _statusVariables = new();
        private readonly ConcurrentDictionary<int, DeviceVariable> _dataVariables = new();

        // 集合事件
        private readonly ConcurrentDictionary<int, string> _collectionEvents = new();

        // 消息处理器
        private readonly ConcurrentDictionary<string, Func<SecsMessage, Task<SecsMessage?>>> _messageHandlers = new();

        // 消息历史
        private readonly ConcurrentQueue<SecsMessage> _messageHistory = new();
        private const int MaxHistorySize = 500;

        public GemCommunicationState CommunicationState => _communicationState;
        public GemControlState ControlState => _controlState;
        public GemProcessingState ProcessingState => _processingState;

        public SecsGemService(IEventBus eventBus, ILogService logService)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            RegisterDefaultHandlers();
        }

        /// <summary>启用 GEM 通信</summary>
        public void EnableCommunication()
        {
            _communicationState = GemCommunicationState.Enabled;
            _logService.Info("SecsGem", "GEM 通信已启用");
        }

        /// <summary>禁用 GEM 通信</summary>
        public void DisableCommunication()
        {
            _communicationState = GemCommunicationState.Disabled;
            _controlState = GemControlState.EquipmentOffline;
            _logService.Info("SecsGem", "GEM 通信已禁用");
        }

        /// <summary>请求上线</summary>
        public bool RequestOnline(bool remoteMode = false)
        {
            if (_communicationState != GemCommunicationState.Enabled)
                return false;

            _controlState = remoteMode ? GemControlState.OnlineRemote : GemControlState.OnlineLocal;
            _logService.Info("SecsGem", $"设备上线：{_controlState}");
            return true;
        }

        /// <summary>请求下线</summary>
        public void GoOffline()
        {
            _controlState = GemControlState.EquipmentOffline;
            _logService.Info("SecsGem", "设备下线");
        }

        /// <summary>设置处理状态</summary>
        public void SetProcessingState(GemProcessingState state)
        {
            _processingState = state;
            _logService.Info("SecsGem", $"处理状态变更：{state}");
        }

        /// <summary>注册状态变量</summary>
        public void RegisterStatusVariable(int svid, string name, object? initialValue = null,
            string unit = "", Type? valueType = null)
        {
            _statusVariables[svid] = new DeviceVariable
            {
                VariableId = svid,
                Name = name,
                Value = initialValue,
                Unit = unit,
                ValueType = valueType ?? typeof(string)
            };
        }

        /// <summary>更新状态变量值</summary>
        public void UpdateStatusVariable(int svid, object value)
        {
            if (_statusVariables.TryGetValue(svid, out var sv))
            {
                sv.Value = value;
                sv.LastUpdated = DateTime.Now;
            }
        }

        /// <summary>获取状态变量值</summary>
        public object? GetStatusVariable(int svid)
        {
            return _statusVariables.TryGetValue(svid, out var sv) ? sv.Value : null;
        }

        /// <summary>注册集合事件</summary>
        public void RegisterCollectionEvent(int ceid, string eventName)
        {
            _collectionEvents[ceid] = eventName;
        }

        /// <summary>触发集合事件</summary>
        public void TriggerCollectionEvent(int ceid)
        {
            if (_collectionEvents.TryGetValue(ceid, out var name))
            {
                _logService.Info("SecsGem", $"集合事件触发：CEID={ceid}, {name}");
            }
        }

        /// <summary>注册 SECS 消息处理器</summary>
        public void RegisterMessageHandler(int stream, int function,
            Func<SecsMessage, Task<SecsMessage?>> handler)
        {
            _messageHandlers[$"S{stream}F{function}"] = handler;
        }

        /// <summary>处理收到的 SECS 消息</summary>
        public async Task<SecsMessage?> HandleMessageAsync(SecsMessage message)
        {
            RecordMessage(message);

            if (_messageHandlers.TryGetValue(message.MessageId, out var handler))
            {
                var reply = await handler(message);
                if (reply != null)
                    RecordMessage(reply);
                return reply;
            }

            _logService.Warning("SecsGem", $"未注册的消息类型：{message.MessageId}");
            return null;
        }

        /// <summary>获取消息历史</summary>
        public IReadOnlyList<SecsMessage> GetMessageHistory()
        {
            return _messageHistory.ToList().AsReadOnly();
        }

        private void RecordMessage(SecsMessage message)
        {
            _messageHistory.Enqueue(message);
            while (_messageHistory.Count > MaxHistorySize)
                _messageHistory.TryDequeue(out _);
        }

        private void RegisterDefaultHandlers()
        {
            // S1F1 — Are You There (在线确认)
            RegisterMessageHandler(1, 1, _ => Task.FromResult<SecsMessage?>(
                new SecsMessage { Stream = 1, Function = 2, Body = "OK" }));

            // S1F13 — Establish Communication (建立通信)
            RegisterMessageHandler(1, 13, _ =>
            {
                _communicationState = GemCommunicationState.Enabled;
                return Task.FromResult<SecsMessage?>(
                    new SecsMessage { Stream = 1, Function = 14, Body = "COMMACK=0" });
            });

            // S1F17 — Request Online (请求上线)
            RegisterMessageHandler(1, 17, _ =>
            {
                var success = RequestOnline();
                return Task.FromResult<SecsMessage?>(
                    new SecsMessage { Stream = 1, Function = 18, Body = success ? "ONLACK=0" : "ONLACK=1" });
            });
        }

        public void Dispose()
        {
            _messageHandlers.Clear();
            _statusVariables.Clear();
            _dataVariables.Clear();
        }
    }
}
