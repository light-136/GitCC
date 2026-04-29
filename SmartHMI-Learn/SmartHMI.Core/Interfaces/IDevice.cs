using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface IDevice
{
    string Id { get; }
    string Name { get; }
    DeviceType Type { get; }
    DeviceStatus Status { get; }
    DateTime LastUpdated { get; }
    Task<bool> ConnectAsync();
    Task DisconnectAsync();
    event EventHandler<DeviceStatus>? StatusChanged;
}
