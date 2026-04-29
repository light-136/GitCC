using SmartHMI.Core.Events;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Modules.Device;

public class DeviceManager
{
    private readonly List<DeviceModel> _devices = new();
    private readonly IEventBus _eventBus;
    private readonly ILoggingService _logger;
    private readonly Timer _healthTimer;

    public IReadOnlyList<DeviceModel> Devices => _devices.ToList();
    public event EventHandler<DeviceModel>? DeviceUpdated;

    public DeviceManager(IEventBus eventBus, ILoggingService logger)
    {
        _eventBus = eventBus;
        _logger = logger;
        SeedDevices();
        _healthTimer = new Timer(SimulateHealthCheck, null, 2000, 3000);
    }

    private void SeedDevices()
    {
        _devices.AddRange(new[]
        {
            new DeviceModel { Id = "plc-001", Name = "主控 PLC", Type = DeviceType.PLC, IpAddress = "192.168.1.10", Port = 502, Description = "Siemens S7-1200" },
            new DeviceModel { Id = "io-001", Name = "IO 模块 A", Type = DeviceType.IoModule, IpAddress = "192.168.1.11", Description = "16DI/16DO" },
            new DeviceModel { Id = "axis-x", Name = "X 轴伺服", Type = DeviceType.Axis, Description = "X轴运动控制" },
            new DeviceModel { Id = "axis-y", Name = "Y 轴伺服", Type = DeviceType.Axis, Description = "Y轴运动控制" },
            new DeviceModel { Id = "axis-z", Name = "Z 轴伺服", Type = DeviceType.Axis, Description = "Z轴运动控制" },
            new DeviceModel { Id = "cam-001", Name = "视觉相机 1", Type = DeviceType.Camera, IpAddress = "192.168.1.20", Description = "工业相机 2MP" },
            new DeviceModel { Id = "sensor-temp", Name = "温度传感器", Type = DeviceType.Sensor, Description = "PT100 温度传感器" },
            new DeviceModel { Id = "sensor-press", Name = "压力传感器", Type = DeviceType.Sensor, Description = "0-10MPa 压力传感器" },
        });
    }

    private readonly Random _rng = new();

    private void SimulateHealthCheck(object? _)
    {
        foreach (var device in _devices)
        {
            var oldStatus = device.Status;
            // Simulate occasional status changes
            if (_rng.NextDouble() < 0.05)
            {
                device.Status = device.Status == DeviceStatus.Online
                    ? DeviceStatus.Offline
                    : DeviceStatus.Online;
                device.LastUpdated = DateTime.Now;

                if (oldStatus != device.Status)
                {
                    _eventBus.Publish(new DeviceStatusChangedEvent
                    {
                        DeviceId = device.Id, DeviceName = device.Name,
                        OldStatus = oldStatus, NewStatus = device.Status
                    });
                    DeviceUpdated?.Invoke(this, device);
                }
            }
        }
    }

    public async Task<bool> ConnectDeviceAsync(string deviceId)
    {
        var device = _devices.FirstOrDefault(d => d.Id == deviceId);
        if (device == null) return false;

        device.Status = DeviceStatus.Connecting;
        DeviceUpdated?.Invoke(this, device);
        await Task.Delay(800);

        device.Status = DeviceStatus.Online;
        device.LastUpdated = DateTime.Now;
        DeviceUpdated?.Invoke(this, device);
        _logger.Info($"设备已连接: {device.Name}", "Device");
        _eventBus.Publish(new DeviceStatusChangedEvent
        {
            DeviceId = device.Id, DeviceName = device.Name,
            OldStatus = DeviceStatus.Connecting, NewStatus = DeviceStatus.Online
        });
        return true;
    }

    public async Task DisconnectDeviceAsync(string deviceId)
    {
        var device = _devices.FirstOrDefault(d => d.Id == deviceId);
        if (device == null) return;

        await Task.Delay(200);
        device.Status = DeviceStatus.Offline;
        device.LastUpdated = DateTime.Now;
        DeviceUpdated?.Invoke(this, device);
        _logger.Info($"设备已断开: {device.Name}", "Device");
    }
}
