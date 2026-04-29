using SmartHMI.Core.Events;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Modules.Communication;

public class CommunicationManager : ICommunicationService
{
    private readonly List<CommunicationChannel> _channels = new();
    private readonly Dictionary<string, TcpCommunicationClient> _tcpClients = new();
    private readonly IEventBus _eventBus;
    private readonly ILoggingService _logger;

    public IReadOnlyList<CommunicationChannel> Channels => _channels.ToList();
    public event EventHandler<CommunicationDataEventArgs>? DataReceived;
    public event EventHandler<CommunicationStatusEventArgs>? StatusChanged;

    public CommunicationManager(IEventBus eventBus, ILoggingService logger)
    {
        _eventBus = eventBus;
        _logger = logger;
        InitializeDefaultChannels();
    }

    private void InitializeDefaultChannels()
    {
        _channels.Add(new CommunicationChannel
        {
            Id = "tcp-main", Name = "主控 TCP", Protocol = ProtocolType.Tcp,
            Endpoint = "127.0.0.1:9000"
        });
        _channels.Add(new CommunicationChannel
        {
            Id = "serial-plc", Name = "PLC 串口", Protocol = ProtocolType.Serial,
            Endpoint = "COM1:9600"
        });
        _channels.Add(new CommunicationChannel
        {
            Id = "mqtt-cloud", Name = "云端 MQTT", Protocol = ProtocolType.Mqtt,
            Endpoint = "mqtt://broker.local:1883"
        });
        _channels.Add(new CommunicationChannel
        {
            Id = "opcua-server", Name = "OPC UA 服务器", Protocol = ProtocolType.OpcUa,
            Endpoint = "opc.tcp://localhost:4840"
        });
    }

    public async Task<bool> ConnectAsync(string channelId)
    {
        var channel = _channels.FirstOrDefault(c => c.Id == channelId);
        if (channel == null) return false;

        _logger.Info($"正在连接通道: {channel.Name}", "Communication");
        channel.Status = ConnectionStatus.Connecting;
        NotifyStatusChanged(channel);

        if (channel.Protocol == ProtocolType.Tcp)
        {
            var parts = channel.Endpoint.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var port))
            {
                var client = new TcpCommunicationClient(parts[0], port);
                client.StatusChanged += (_, s) =>
                {
                    channel.Status = s;
                    if (s == ConnectionStatus.Connected) channel.LastConnectedAt = DateTime.Now;
                    NotifyStatusChanged(channel);
                    _eventBus.Publish(new CommunicationStatusChangedEvent
                    {
                        ChannelId = channel.Id, ChannelName = channel.Name, Status = s
                    });
                };
                client.DataReceived += (_, data) =>
                {
                    channel.TotalBytesReceived += data.Length;
                    channel.LastMessageAt = DateTime.Now;
                    DataReceived?.Invoke(this, new CommunicationDataEventArgs
                    {
                        ChannelId = channelId, Data = data
                    });
                };
                _tcpClients[channelId] = client;
                return await client.ConnectAsync();
            }
        }

        // Simulate connection for non-TCP protocols
        await Task.Delay(500);
        channel.Status = ConnectionStatus.Connected;
        channel.LastConnectedAt = DateTime.Now;
        NotifyStatusChanged(channel);
        _logger.Info($"通道已连接: {channel.Name}", "Communication");
        return true;
    }

    public async Task DisconnectAsync(string channelId)
    {
        var channel = _channels.FirstOrDefault(c => c.Id == channelId);
        if (channel == null) return;

        if (_tcpClients.TryGetValue(channelId, out var client))
        {
            await client.DisconnectAsync();
            _tcpClients.Remove(channelId);
        }
        else
        {
            channel.Status = ConnectionStatus.Disconnected;
            NotifyStatusChanged(channel);
        }
        _logger.Info($"通道已断开: {channel.Name}", "Communication");
    }

    public async Task<bool> SendAsync(string channelId, byte[] data)
    {
        var channel = _channels.FirstOrDefault(c => c.Id == channelId);
        if (channel == null) return false;

        if (_tcpClients.TryGetValue(channelId, out var client))
        {
            var result = await client.SendAsync(data);
            if (result) channel.TotalBytesSent += data.Length;
            return result;
        }

        // Simulate send for other protocols
        channel.TotalBytesSent += data.Length;
        channel.LastMessageAt = DateTime.Now;
        return true;
    }

    private void NotifyStatusChanged(CommunicationChannel channel)
    {
        StatusChanged?.Invoke(this, new CommunicationStatusEventArgs
        {
            ChannelId = channel.Id, Status = channel.Status
        });
    }
}
