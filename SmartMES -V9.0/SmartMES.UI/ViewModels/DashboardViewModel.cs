using SmartMES.Core.Infrastructure;
using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace SmartMES.UI.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly ILoggingService _logger;
        private readonly IAlarmService _alarmService;
        private readonly ISettingsService _settings;
        private DispatcherTimer? _timer;
        private readonly Random _random = new();

        private double _temperature;
        public double Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }

        private double _pressure;
        public double Pressure { get => _pressure; set => SetProperty(ref _pressure, value); }

        private double _speed;
        public double Speed { get => _speed; set => SetProperty(ref _speed, value); }

        private bool _isMonitoring;
        public bool IsMonitoring
        {
            get => _isMonitoring;
            set
            {
                if (!SetProperty(ref _isMonitoring, value)) return;
                StartMonitorCommand.RaiseCanExecuteChanged();
                StopMonitorCommand.RaiseCanExecuteChanged();
            }
        }

        private string _statusText = "监控已停止";
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        public ObservableCollection<DataPoint> TemperatureHistory { get; } = new();
        public ObservableCollection<DataPoint> PressureHistory { get; } = new();
        public ObservableCollection<DataPoint> SpeedHistory { get; } = new();

        public RelayCommand StartMonitorCommand { get; }
        public RelayCommand StopMonitorCommand { get; }

        private const int MaxHistoryPoints = 60;

        public DashboardViewModel(ILoggingService logger, IAlarmService alarmService, ISettingsService settings)
        {
            _logger = logger;
            _alarmService = alarmService;
            _settings = settings;

            StartMonitorCommand = new RelayCommand(_ => StartMonitor(), _ => !_isMonitoring);
            StopMonitorCommand = new RelayCommand(_ => StopMonitor(), _ => _isMonitoring);

            _temperature = 45 + _random.NextDouble() * 20;
            _pressure = 5 + _random.NextDouble() * 3;
            _speed = 1500 + _random.NextDouble() * 500;
        }

        private void StartMonitor()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_settings.Settings.DataSamplingIntervalMs)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
            IsMonitoring = true;
            StatusText = "监控运行中";
            _logger.LogInfo("仪表盘监控已启动", "Dashboard");
        }

        private void StopMonitor()
        {
            _timer?.Stop();
            IsMonitoring = false;
            StatusText = "监控已停止";
            _logger.LogInfo("仪表盘监控已停止", "Dashboard");
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            _temperature = Math.Max(20, Math.Min(120, _temperature + (_random.NextDouble() - 0.48) * 2.5));
            _pressure = Math.Max(0, Math.Min(15, _pressure + (_random.NextDouble() - 0.5) * 0.5));
            _speed = Math.Max(0, Math.Min(4000, _speed + (_random.NextDouble() - 0.5) * 80));

            Temperature = Math.Round(_temperature, 1);
            Pressure = Math.Round(_pressure, 2);
            Speed = Math.Round(_speed, 0);

            var now = DateTime.Now;
            AddPoint(TemperatureHistory, now, Temperature, "温度");
            AddPoint(PressureHistory, now, Pressure, "压力");
            AddPoint(SpeedHistory, now, Speed, "速度");

            CheckAlarms();
        }

        private void AddPoint(ObservableCollection<DataPoint> collection, DateTime time, double value, string name)
        {
            collection.Add(new DataPoint { Timestamp = time, Value = value, DeviceName = name });
            while (collection.Count > MaxHistoryPoints)
                collection.RemoveAt(0);
        }

        private void CheckAlarms()
        {
            var s = _settings.Settings;
            if (_temperature > s.TemperatureAlarmThreshold)
                _alarmService.TriggerAlarm(
                    "ALM-T001",
                    $"温度超限: {_temperature:F1}℃ (>{s.TemperatureAlarmThreshold})",
                    AlarmLevel.Warning);

            if (_pressure > s.PressureAlarmThreshold)
                _alarmService.TriggerAlarm(
                    "ALM-P001",
                    $"压力超限: {_pressure:F2}MPa (>{s.PressureAlarmThreshold})",
                    AlarmLevel.Critical);
        }
    }
}
