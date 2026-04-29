namespace SmartHMI.Core.Models;

public enum SecsMessageDirection { HostToEquipment, EquipmentToHost }

public class SecsGemMessage
{
    public int Stream { get; set; }
    public int Function { get; set; }
    public SecsMessageDirection Direction { get; set; }
    public bool ReplyExpected { get; set; }
    public string SystemBytes { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public Dictionary<string, object> Data { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Description => $"S{Stream}F{Function}";
}

public enum SecsGemState { Disabled, Enabled, Selected, Online, OnlineLocal, OnlineRemote }

public class SecsGemStatus
{
    public SecsGemState State { get; set; } = SecsGemState.Disabled;
    public string EquipmentId { get; set; } = "EQ-001";
    public string HostIp { get; set; } = "127.0.0.1";
    public int HostPort { get; set; } = 5000;
    public DateTime? LastHeartbeat { get; set; }
    public int MessagesSent { get; set; }
    public int MessagesReceived { get; set; }
}
