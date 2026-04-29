using System.Collections.ObjectModel;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using SmartHMI.Services;

namespace SmartHMI.UI.ViewModels;

public class LogViewModel : BaseViewModel
{
    private readonly LoggingService _loggingService;
    private string _filterModule = "全部";
    private string _filterLevel = "全部";

    public ObservableCollection<LogEntry> Entries { get; } = new();
    public string FilterModule { get => _filterModule; set { SetField(ref _filterModule, value); Refresh(); } }
    public string FilterLevel { get => _filterLevel; set { SetField(ref _filterLevel, value); Refresh(); } }

    public List<string> ModuleOptions { get; } = ["全部", "System", "Communication", "Device", "Motion", "Alarm"];
    public List<string> LevelOptions { get; } = ["全部", "Debug", "Info", "Warning", "Error"];

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ClearCommand { get; }

    public LogViewModel(LoggingService loggingService)
    {
        _loggingService = loggingService;
        _loggingService.EntryAdded += (_, e) => App.Current.Dispatcher.Invoke(() =>
        {
            if (MatchesFilter(e))
            {
                Entries.Insert(0, e);
                if (Entries.Count > 500) Entries.RemoveAt(Entries.Count - 1);
            }
        });

        RefreshCommand = new RelayCommand(Refresh);
        ClearCommand = new RelayCommand(() => Entries.Clear());

        Refresh();
    }

    private void Refresh()
    {
        Entries.Clear();
        var entries = _loggingService.GetRecent(500);
        foreach (var e in entries.Reverse().Where(MatchesFilter))
            Entries.Add(e);
    }

    private bool MatchesFilter(LogEntry e)
    {
        if (FilterModule != "全部" && e.Module != FilterModule) return false;
        if (FilterLevel != "全部" && e.Level.ToString() != FilterLevel) return false;
        return true;
    }
}
