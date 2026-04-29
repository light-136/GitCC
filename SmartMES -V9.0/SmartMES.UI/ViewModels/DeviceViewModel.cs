using SmartMES.Core.Infrastructure;
using SmartMES.Core.Interfaces;
using SmartMES.Services.Device;
using System.Collections.ObjectModel;

namespace SmartMES.UI.ViewModels
{
    public class DeviceViewModel : ViewModelBase
    {
        private readonly ILoggingService _logger;
        private readonly IEventBus _eventBus;
        private DeviceItemViewModel? _selectedDevice;

        public ObservableCollection<DeviceItemViewModel> Devices { get; } = new();

        public DeviceItemViewModel? SelectedDevice
        {
            get => _selectedDevice;
            set => SetProperty(ref _selectedDevice, value);
        }

        public RelayCommand ConnectAllCommand { get; }
        public RelayCommand DisconnectAllCommand { get; }
        public RelayCommand<DeviceItemViewModel> ConnectCommand { get; }
        public RelayCommand<DeviceItemViewModel> DisconnectCommand { get; }

        public DeviceViewModel(ILoggingService logger, IEventBus eventBus)
        {
            _logger = logger;
            _eventBus = eventBus;

            Devices.Add(new DeviceItemViewModel(new PlcDevice("西门子S7-1200 #1"), logger, eventBus));
            Devices.Add(new DeviceItemViewModel(new PlcDevice("三菱Q系列 #2"), logger, eventBus));
            Devices.Add(new DeviceItemViewModel(new SensorDevice("温度传感器 #01", "Temperature", 0, 150), logger, eventBus));
            Devices.Add(new DeviceItemViewModel(new SensorDevice("压力传感器 #01", "Pressure", 0, 20), logger, eventBus));
            Devices.Add(new DeviceItemViewModel(new SensorDevice("速度传感器 #01", "Speed", 0, 5000), logger, eventBus));

            ConnectAllCommand = new RelayCommand(async _ =>
            {
                foreach (var d in Devices)
                    await d.ConnectAsync();
            });

            DisconnectAllCommand = new RelayCommand(async _ =>
            {
                foreach (var d in Devices)
                    await d.DisconnectAsync();
            });

            ConnectCommand = new RelayCommand<DeviceItemViewModel>(
                async d => { if (d != null) await d.ConnectAsync(); });

            DisconnectCommand = new RelayCommand<DeviceItemViewModel>(
                async d => { if (d != null) await d.DisconnectAsync(); });
        }
    }

    public class DeviceItemViewModel : ViewModelBase
    {
        private readonly IDevice _device;
        private readonly ILoggingService _logger;
        private readonly IEventBus _eventBus;
        private bool _isConnecting;

        public string Name => _device.Name;
        public string DeviceType => _device.DeviceType;

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set => SetProperty(ref _isConnected, value);
        }

        private string _status = "未连接";
        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        private double _lastValue;
        public double LastValue
        {
            get => _lastValue;
            private set => SetProperty(ref _lastValue, value);
        }

        private DateTime _lastUpdated = DateTime.Now;
        public DateTime LastUpdated
        {
            get => _lastUpdated;
            private set => SetProperty(ref _lastUpdated, value);
        }

        public bool IsConnecting
        {
            get => _isConnecting;
            private set => SetProperty(ref _isConnecting, value);
        }

        public DeviceItemViewModel(IDevice device, ILoggingService logger, IEventBus eventBus)
        {
            _device = device;
            _logger = logger;
            _eventBus = eventBus;

            _device.StatusChanged += (_, e) =>
            {
                Status = e.NewStatus;
                IsConnected = e.IsConnected;
                _eventBus.Publish(new DeviceStatusEvent
                {
                    DeviceId = _device.Id,
                    DeviceName = _device.Name,
                    IsConnected = e.IsConnected,
                    Status = e.NewStatus
                });
            };
        }

        public async Task ConnectAsync()
        {
            if (_isConnected || _isConnecting) return;
            IsConnecting = true;
            try { await _device.ConnectAsync(); }
            catch (Exception ex) { _logger.LogError($"{Name} 连接失败: {ex.Message}", "Device"); }
            finally { IsConnecting = false; }
        }

        public async Task DisconnectAsync()
        {
            if (!_isConnected) return;
            try { await _device.DisconnectAsync(); }
            catch (Exception ex) { _logger.LogError($"{Name} 断开失败: {ex.Message}", "Device"); }
        }

        public async Task<double> ReadValueAsync()
        {
            if (!_isConnected) return 0;
            try
            {
                var val = await _device.ReadDataAsync();
                LastValue = val;
                LastUpdated = DateTime.Now;
                return val;
            }
            catch
            {
                return 0;
            }
        }
    }
}
