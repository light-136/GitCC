using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Modules.SecsGem;

/// <summary>
/// SECS/GEM 抽象层实现（仿真模式）
/// 实现 SEMI E5/E30/E37 标准的核心消息集：
///   S1F1/S1F2 — 心跳（Are You There / I Am Online）
///   S1F13/S1F14 — 上线请求
///   S2F41/S2F42 — 主机命令
///   S6F11/S6F12 — 事件上报
/// </summary>
public class SecsGemService : ISecsGemService
{
    private readonly List<SecsGemMessage> _log = new();
    private readonly Lock _lock = new();
    private System.Timers.Timer? _heartbeatTimer;

    public SecsGemStatus Status { get; } = new();

    public event EventHandler<SecsGemMessage>? MessageReceived;
    public event EventHandler<SecsGemMessage>? MessageSent;

    public async Task<bool> EnableAsync()
    {
        await Task.Delay(200);
        Status.State = SecsGemState.Enabled;
        LogMessage(new SecsGemMessage { Stream = 1, Function = 13, Direction = SecsMessageDirection.EquipmentToHost, ReplyExpected = true, Data = new() { ["COMMACK"] = 0 } });
        return true;
    }

    public async Task DisableAsync()
    {
        _heartbeatTimer?.Stop();
        await Task.Delay(100);
        Status.State = SecsGemState.Disabled;
    }

    public async Task<bool> GoOnlineAsync()
    {
        if (Status.State < SecsGemState.Enabled) return false;
        await Task.Delay(300);
        Status.State = SecsGemState.OnlineRemote;

        // 启动心跳定时器（每 10 秒）
        _heartbeatTimer = new System.Timers.Timer(10_000);
        _heartbeatTimer.Elapsed += async (_, _) => await SendS1F1HeartbeatAsync();
        _heartbeatTimer.Start();

        // 发送 S1F13 上线请求
        var msg = new SecsGemMessage
        {
            Stream = 1, Function = 13,
            Direction = SecsMessageDirection.EquipmentToHost,
            ReplyExpected = true,
            Data = new() { ["COMMACK"] = 0, ["MDLN"] = "SmartHMI", ["SOFTREV"] = "1.0" }
        };
        LogMessage(msg);
        MessageSent?.Invoke(this, msg);

        // 仿真主机回复 S1F14
        await Task.Delay(100);
        var reply = new SecsGemMessage { Stream = 1, Function = 14, Direction = SecsMessageDirection.HostToEquipment, Data = new() { ["COMMACK"] = 0 } };
        LogMessage(reply);
        MessageReceived?.Invoke(this, reply);

        return true;
    }

    public async Task GoOfflineAsync()
    {
        _heartbeatTimer?.Stop();
        await Task.Delay(100);
        Status.State = SecsGemState.Enabled;
    }

    public async Task<bool> SendS1F1HeartbeatAsync()
    {
        if (Status.State < SecsGemState.OnlineRemote) return false;

        var msg = new SecsGemMessage { Stream = 1, Function = 1, Direction = SecsMessageDirection.EquipmentToHost, ReplyExpected = true };
        LogMessage(msg);
        MessageSent?.Invoke(this, msg);
        Status.LastHeartbeat = DateTime.Now;
        Status.MessagesSent++;

        await Task.Delay(50);
        var reply = new SecsGemMessage { Stream = 1, Function = 2, Direction = SecsMessageDirection.HostToEquipment, Data = new() { ["MDLN"] = "HOST", ["SOFTREV"] = "1.0" } };
        LogMessage(reply);
        MessageReceived?.Invoke(this, reply);
        Status.MessagesReceived++;

        return true;
    }

    public async Task SendEventAsync(string eventName, Dictionary<string, object> data)
    {
        // S6F11 — 事件上报
        var msg = new SecsGemMessage
        {
            Stream = 6, Function = 11,
            Direction = SecsMessageDirection.EquipmentToHost,
            ReplyExpected = true,
            Data = new Dictionary<string, object>(data) { ["CEID"] = eventName, ["TIMESTAMP"] = DateTime.Now.ToString("o") }
        };
        LogMessage(msg);
        MessageSent?.Invoke(this, msg);
        Status.MessagesSent++;

        await Task.Delay(50);
        var ack = new SecsGemMessage { Stream = 6, Function = 12, Direction = SecsMessageDirection.HostToEquipment, Data = new() { ["ACKC6"] = 0 } };
        LogMessage(ack);
        MessageReceived?.Invoke(this, ack);
        Status.MessagesReceived++;
    }

    public IReadOnlyList<SecsGemMessage> GetMessageLog(int count = 100)
    { lock (_lock) return _log.TakeLast(count).ToList(); }

    private void LogMessage(SecsGemMessage msg)
    {
        lock (_lock)
        {
            _log.Add(msg);
            if (_log.Count > 500) _log.RemoveAt(0);
        }
    }
}
