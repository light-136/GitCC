namespace SmartHMI.Core.Models;

public enum ConnectionStatus { Disconnected, Connecting, Connected, Reconnecting, Faulted }
public enum ProtocolType { Tcp, Serial, Http, Mqtt, OpcUa, ModbusTcp, ModbusRtu }

public class CommunicationChannel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public ProtocolType Protocol { get; set; }
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;
    public string Endpoint { get; set; } = "";
    public DateTime? LastConnectedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public long TotalBytesSent { get; set; }
    public long TotalBytesReceived { get; set; }
    public int ReconnectCount { get; set; }
    public List<string> MessageLog { get; set; } = new();
}
