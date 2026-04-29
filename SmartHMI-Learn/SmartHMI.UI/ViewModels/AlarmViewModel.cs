using System.Collections.ObjectModel;
using SmartHMI.Core.Events;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.UI.ViewModels;

public class AlarmViewModel : BaseViewModel
{
    private readonly IAlarmService _alarmService;
    private AlarmRecord? _selectedAlarm;
    private string _filterText = "";

    public ObservableCollection<AlarmRecord> ActiveAlarms { get; } = new();
    public ObservableCollection<AlarmRecord> HistoryAlarms { get; } = new();
    public AlarmRecord? SelectedAlarm { get => _selectedAlarm; set => SetField(ref _selectedAlarm, value); }
    public string FilterText { get => _filterText; set { SetField(ref _filterText, value); ApplyFilter(); } }

    public RelayCommand AcknowledgeCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public AlarmViewModel(IAlarmService alarmService, IEventBus eventBus)
    {
        _alarmService = alarmService;

        AcknowledgeCommand = new RelayCommand(_ => AcknowledgeSelected(),
            _ => SelectedAlarm?.IsAcknowledged == false);
        ClearCommand = new RelayCommand(_ => ClearSelected(),
            _ => SelectedAlarm != null);
        ClearAllCommand = new RelayCommand(_ => { _alarmService.ClearAll(); Refresh(); });
        RefreshCommand = new RelayCommand(Refresh);

        eventBus.Subscribe<NewAlarmEvent>(_ => App.Current.Dispatcher.Invoke(Refresh));
        eventBus.Subscribe<AlarmClearedEvent>(_ => App.Current.Dispatcher.Invoke(Refresh));

        Refresh();
    }

    private void Refresh()
    {
        ActiveAlarms.Clear();
        foreach (var a in _alarmService.ActiveAlarms)
            ActiveAlarms.Add(a);

        HistoryAlarms.Clear();
        foreach (var a in _alarmService.AlarmHistory.TakeLast(100).Reverse())
            HistoryAlarms.Add(a);
    }

    private void ApplyFilter()
    {
        // Filter is applied via CollectionView in the View
    }

    private void AcknowledgeSelected()
    {
        if (SelectedAlarm == null) return;
        _alarmService.Acknowledge(SelectedAlarm.Id);
        Refresh();
    }

    private void ClearSelected()
    {
        if (SelectedAlarm == null) return;
        _alarmService.Clear(SelectedAlarm.Id);
        Refresh();
    }
}
