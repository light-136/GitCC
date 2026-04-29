using SmartMES.Core.Infrastructure;
using SmartMES.Core.Interfaces;

namespace SmartMES.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly ILoggingService _logger;

        private double _tempThreshold;
        public double TempThreshold { get => _tempThreshold; set => SetProperty(ref _tempThreshold, value); }

        private double _pressureThreshold;
        public double PressureThreshold { get => _pressureThreshold; set => SetProperty(ref _pressureThreshold, value); }

        private double _speedThreshold;
        public double SpeedThreshold { get => _speedThreshold; set => SetProperty(ref _speedThreshold, value); }

        private string _tcpServerIp = string.Empty;
        public string TcpServerIp { get => _tcpServerIp; set => SetProperty(ref _tcpServerIp, value); }

        private int _tcpPort;
        public int TcpPort { get => _tcpPort; set => SetProperty(ref _tcpPort, value); }

        private string _serialPort = string.Empty;
        public string SerialPort { get => _serialPort; set => SetProperty(ref _serialPort, value); }

        private int _samplingInterval;
        public int SamplingInterval { get => _samplingInterval; set => SetProperty(ref _samplingInterval, value); }

        private string _saveStatus = string.Empty;
        public string SaveStatus { get => _saveStatus; set => SetProperty(ref _saveStatus, value); }

        public RelayCommand SaveCommand { get; }
        public RelayCommand ReloadCommand { get; }

        public SettingsViewModel(ISettingsService settingsService, ILoggingService logger)
        {
            _settingsService = settingsService;
            _logger = logger;

            LoadFromSettings();

            SaveCommand = new RelayCommand(async _ =>
            {
                ApplyToSettings();
                await _settingsService.SaveAsync();
                SaveStatus = $"已保存 ({DateTime.Now:HH:mm:ss})";
                _logger.LogInfo("系统配置已保存", "Settings");
            });

            ReloadCommand = new RelayCommand(async _ =>
            {
                await _settingsService.LoadAsync();
                LoadFromSettings();
                SaveStatus = "已重新加载";
            });
        }

        private void LoadFromSettings()
        {
            var s = _settingsService.Settings;
            TempThreshold = s.TemperatureAlarmThreshold;
            PressureThreshold = s.PressureAlarmThreshold;
            SpeedThreshold = s.SpeedAlarmThreshold;
            TcpServerIp = s.TcpServerIp;
            TcpPort = s.TcpServerPort;
            SerialPort = s.SerialPortName;
            SamplingInterval = s.DataSamplingIntervalMs;
        }

        private void ApplyToSettings()
        {
            var s = _settingsService.Settings;
            s.TemperatureAlarmThreshold = TempThreshold;
            s.PressureAlarmThreshold = PressureThreshold;
            s.SpeedAlarmThreshold = SpeedThreshold;
            s.TcpServerIp = TcpServerIp;
            s.TcpServerPort = TcpPort;
            s.SerialPortName = SerialPort;
            s.DataSamplingIntervalMs = SamplingInterval;
        }
    }
}
