using System.Collections.ObjectModel;
using System.Windows.Threading;
using SmartHMI.Core.Events;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.IO;
using SmartHMI.Core.Models;
using SmartHMI.Modules.Device;

namespace SmartHMI.UI.ViewModels;

public class DashboardViewModel : BaseViewModel
{
    private readonly IAlarmService _alarmService;
    private readonly ILoggingService _logger;
    private readonly DeviceManager _deviceManager;
    private readonly IIoDevice _ioDevice;
    private readonly IEventBus _eventBus;

    public ObservableCollection<AlarmRecord> RecentAlarms { get; } = new();
    public ObservableCollection<IoChannelDisplay> IoChannels { get; } = new();
    public ObservableCollection<DeviceStatusDisplay> DeviceStatuses { get; } = new();

    private int _onlineDeviceCount;
    private int _activeAlarmCount;
    private string _systemState = "运行中";
    private double _ai0Value, _ai1Value, _ai2Value, _ai3Value;

    public int OnlineDeviceCount { get => _onlineDeviceCount; set => SetField(ref _onlineDeviceCount, value); }
    public int ActiveAlarmCount { get => _activeAlarmCount; set => SetField(ref _activeAlarmCount, value); }
    public string SystemState { get => _systemState; set => SetField(ref _systemState, value); }
    public double Ai0Value { get => _ai0Value; set => SetField(ref _ai0Value, value); }
    public double Ai1Value { get => _ai1Value; set => SetField(ref _ai1Value, value); }
    public double Ai2Value { get => _ai2Value; set => SetField(ref _ai2Value, value); }
    public double Ai3Value { get => _ai3Value; set => SetField(ref _ai3Value, value); }

    public DashboardViewModel(IAlarmService alarmService, ILoggingService logger,
        DeviceManager deviceManager, IIoDevice ioDevice, IEventBus eventBus)
    {
        _alarmService = alarmService;
        _logger = logger;
        _deviceManager = deviceManager;
        _ioDevice = ioDevice;
        _eventBus = eventBus;

        _eventBus.Subscribe<NewAlarmEvent>(OnNewAlarm);
        _eventBus.Subscribe<DeviceStatusChangedEvent>(OnDeviceStatusChanged);
        _ioDevice.ChannelChanged += OnIoChannelChanged;

        RefreshAll();
    }

    private void RefreshAll()
    {
        ActiveAlarmCount = _alarmService.ActiveAlarms.Count;
        OnlineDeviceCount = _deviceManager.Devices.Count(d => d.Status == DeviceStatus.Online);

        RecentAlarms.Clear();
        foreach (var a in _alarmService.ActiveAlarms.TakeLast(5))
            RecentAlarms.Add(a);

        DeviceStatuses.Clear();
        foreach (var d in _deviceManager.Devices)
            DeviceStatuses.Add(new DeviceStatusDisplay(d));

        IoChannels.Clear();
        foreach (var ch in _ioDevice.GetChannels().Where(c => c.Type == IoChannelType.DigitalInput).Take(8))
            IoChannels.Add(new IoChannelDisplay(ch));

        UpdateAnalogValues();
    }

    private void UpdateAnalogValues()
    {
        Ai0Value = _ioDevice.ReadAnalog(200);
        Ai1Value = _ioDevice.ReadAnalog(201);
        Ai2Value = _ioDevice.ReadAnalog(202);
        Ai3Value = _ioDevice.ReadAnalog(203);
    }

    private void OnNewAlarm(NewAlarmEvent e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            ActiveAlarmCount = _alarmService.ActiveAlarms.Count;
            if (RecentAlarms.Count >= 5) RecentAlarms.RemoveAt(0);
            RecentAlarms.Add(e.Alarm);
        });
    }

    private void OnDeviceStatusChanged(DeviceStatusChangedEvent e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            OnlineDeviceCount = _deviceManager.Devices.Count(d => d.Status == DeviceStatus.Online);
            var display = DeviceStatuses.FirstOrDefault(d => d.Id == e.DeviceId);
            if (display != null) display.Status = e.NewStatus.ToString();
        });
    }

    private void OnIoChannelChanged(object? sender, IoChannel ch)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (ch.Type == IoChannelType.AnalogInput) UpdateAnalogValues();
            var display = IoChannels.FirstOrDefault(c => c.Address == ch.Address);
            if (display != null) display.Value = ch.AsBool();
        });
    }
}

public class IoChannelDisplay : BaseViewModel
{
    private bool _value;
    public int Address { get; }
    public string Name { get; }
    public bool Value { get => _value; set => SetField(ref _value, value); }
    public IoChannelDisplay(IoChannel ch) { Address = ch.Address; Name = ch.Name; Value = ch.AsBool(); }
}

public class DeviceStatusDisplay : BaseViewModel
{
    private string _status = "";
    public string Id { get; }
    public string Name { get; }
    public string Type { get; }
    public string Status { get => _status; set => SetField(ref _status, value); }
    public DeviceStatusDisplay(DeviceModel d) { Id = d.Id; Name = d.Name; Type = d.Type.ToString(); Status = d.Status.ToString(); }
}
