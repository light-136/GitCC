using SmartMES.Core.Infrastructure;
using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartMES.UI.ViewModels
{
    public class AlarmViewModel : ViewModelBase
    {
        private readonly IAlarmService _alarmService;
        private readonly ILoggingService _logger;
        private AlarmItemViewModel? _selectedAlarm;

        public ObservableCollection<AlarmItemViewModel> Alarms { get; } = new();

        public AlarmItemViewModel? SelectedAlarm
        {
            get => _selectedAlarm;
            set
            {
                if (!SetProperty(ref _selectedAlarm, value)) return;
                AcknowledgeCommand.RaiseCanExecuteChanged();
            }
        }

        private int _activeCount;
        public int ActiveCount { get => _activeCount; set => SetProperty(ref _activeCount, value); }

        public RelayCommand AcknowledgeCommand { get; }
        public RelayCommand AcknowledgeAllCommand { get; }
        public RelayCommand ClearAcknowledgedCommand { get; }
        public RelayCommand TriggerTestAlarmCommand { get; }

        public AlarmViewModel(IAlarmService alarmService, ILoggingService logger)
        {
            _alarmService = alarmService;
            _logger = logger;

            _alarmService.AlarmTriggered += (_, alarm) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Alarms.Add(new AlarmItemViewModel(alarm));
                    RefreshCounts();
                });
            };

            _alarmService.AlarmAcknowledged += (_, alarm) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var vm = Alarms.FirstOrDefault(a => a.Id == alarm.Id);
                    vm?.Refresh(alarm);
                    RefreshCounts();
                });
            };

            AcknowledgeCommand = new RelayCommand(
                _ => AcknowledgeSelected(),
                _ => _selectedAlarm != null && _selectedAlarm.Status == AlarmStatus.Active);

            AcknowledgeAllCommand = new RelayCommand(_ => AcknowledgeAll());

            ClearAcknowledgedCommand = new RelayCommand(_ =>
            {
                _alarmService.ClearAcknowledgedAlarms();
                var toRemove = Alarms.Where(a => a.Status == AlarmStatus.Acknowledged).ToList();
                foreach (var a in toRemove) Alarms.Remove(a);
                RefreshCounts();
            });

            TriggerTestAlarmCommand = new RelayCommand(_ =>
            {
                var levels = new[] { AlarmLevel.Info, AlarmLevel.Warning, AlarmLevel.Critical };
                var rnd = new Random();
                _alarmService.TriggerAlarm(
                    $"TEST-{rnd.Next(100, 999)}",
                    $"测试报警 [{DateTime.Now:HH:mm:ss}]",
                    levels[rnd.Next(3)]);
            });
        }

        private void AcknowledgeSelected()
        {
            if (_selectedAlarm == null) return;
            _alarmService.AcknowledgeAlarm(_selectedAlarm.Id);
            _logger.LogInfo($"确认报警: {_selectedAlarm.Code}", "Alarm");
        }

        private void AcknowledgeAll()
        {
            foreach (var alarm in Alarms.Where(a => a.Status == AlarmStatus.Active).ToList())
                _alarmService.AcknowledgeAlarm(alarm.Id);
            _logger.LogInfo("已执行全部报警确认", "Alarm");
        }

        private void RefreshCounts()
        {
            ActiveCount = Alarms.Count(a => a.Status == AlarmStatus.Active);
            AcknowledgeCommand.RaiseCanExecuteChanged();
        }
    }

    public class AlarmItemViewModel : ViewModelBase
    {
        private AlarmRecord _record;

        public Guid Id => _record.Id;
        public string Code => _record.Code;
        public string Message => _record.Message;
        public AlarmLevel Level => _record.Level;

        public string LevelText => _record.Level switch
        {
            AlarmLevel.Critical => "严重",
            AlarmLevel.Warning => "警告",
            _ => "提示"
        };

        private AlarmStatus _status;
        public AlarmStatus Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public string StatusText => Status switch
        {
            AlarmStatus.Active => "活跃",
            AlarmStatus.Acknowledged => "已确认",
            _ => "已清除"
        };

        public DateTime TriggeredAt => _record.TriggeredAt;
        public DateTime? AcknowledgedAt => _record.AcknowledgedAt;

        public AlarmItemViewModel(AlarmRecord record)
        {
            _record = record;
            _status = record.Status;
        }

        public void Refresh(AlarmRecord updated)
        {
            _record = updated;
            Status = updated.Status;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(AcknowledgedAt));
        }
    }
}
