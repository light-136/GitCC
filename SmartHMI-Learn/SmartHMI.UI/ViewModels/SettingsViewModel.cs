using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.UI.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private SystemSettings _settings;
    private bool _isDirty;

    public SystemSettings Settings { get => _settings; set => SetField(ref _settings, value); }
    public bool IsDirty { get => _isDirty; set => SetField(ref _isDirty, value); }

    public RelayCommand SaveCommand { get; }
    public RelayCommand ReloadCommand { get; }

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Current;

        SaveCommand = new RelayCommand(Save, () => IsDirty);
        ReloadCommand = new RelayCommand(Reload);
    }

    private void Save()
    {
        _settingsService.Save();
        IsDirty = false;
    }

    private void Reload()
    {
        _settingsService.Reload();
        Settings = _settingsService.Current;
        IsDirty = false;
    }

    public void MarkDirty() => IsDirty = true;
}
