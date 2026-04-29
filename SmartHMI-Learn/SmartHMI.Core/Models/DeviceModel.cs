namespace SmartHMI.Core.Models;

public enum DeviceType { PLC, Sensor, IoModule, Axis, Camera, Controller, Instrument, Other }
public enum DeviceStatus { Offline, Connecting, Online, Faulted, Maintenance }

public class DeviceModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public DeviceType Type { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Offline;
    public string IpAddress { get; set; } = "";
    public int Port { get; set; }
    public string Description { get; set; } = "";
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public Dictionary<string, string> Properties { get; set; } = new();
}
