using System.Collections.ObjectModel;
using SmartHMI.Core.Models;
using SmartHMI.Modules.Device;

namespace SmartHMI.UI.ViewModels;

public class DeviceViewModel : BaseViewModel
{
    private readonly DeviceManager _deviceManager;
    private DeviceModel? _selectedDevice;
    private bool _isBusy;

    public ObservableCollection<DeviceModel> Devices { get; } = new();
    public DeviceModel? SelectedDevice { get => _selectedDevice; set => SetField(ref _selectedDevice, value); }
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }

    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public DeviceViewModel(DeviceManager deviceManager)
    {
        _deviceManager = deviceManager;
        _deviceManager.DeviceUpdated += (_, d) => App.Current.Dispatcher.Invoke(RefreshDevices);

        ConnectCommand = new RelayCommand(async _ => await ConnectSelectedAsync(),
            _ => SelectedDevice?.Status == DeviceStatus.Offline && !IsBusy);
        DisconnectCommand = new RelayCommand(async _ => await DisconnectSelectedAsync(),
            _ => SelectedDevice?.Status == DeviceStatus.Online && !IsBusy);
        RefreshCommand = new RelayCommand(RefreshDevices);

        RefreshDevices();
    }

    private void RefreshDevices()
    {
        Devices.Clear();
        foreach (var d in _deviceManager.Devices)
            Devices.Add(d);
    }

    private async Task ConnectSelectedAsync()
    {
        if (SelectedDevice == null) return;
        IsBusy = true;
        await _deviceManager.ConnectDeviceAsync(SelectedDevice.Id);
        IsBusy = false;
        RefreshDevices();
    }

    private async Task DisconnectSelectedAsync()
    {
        if (SelectedDevice == null) return;
        IsBusy = true;
        await _deviceManager.DisconnectDeviceAsync(SelectedDevice.Id);
        IsBusy = false;
        RefreshDevices();
    }
}
