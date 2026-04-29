using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface ICommunicationService
{
    IReadOnlyList<CommunicationChannel> Channels { get; }
    Task<bool> ConnectAsync(string channelId);
    Task DisconnectAsync(string channelId);
    Task<bool> SendAsync(string channelId, byte[] data);
    event EventHandler<CommunicationDataEventArgs>? DataReceived;
    event EventHandler<CommunicationStatusEventArgs>? StatusChanged;
}

public class CommunicationDataEventArgs : EventArgs
{
    public string ChannelId { get; init; } = "";
    public byte[] Data { get; init; } = [];
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

public class CommunicationStatusEventArgs : EventArgs
{
    public string ChannelId { get; init; } = "";
    public ConnectionStatus Status { get; init; }
}
