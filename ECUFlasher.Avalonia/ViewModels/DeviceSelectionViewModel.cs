using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Communication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NefMotoECUFlasher.Avalonia.ViewModels;

public partial class DeviceSelectionViewModel : ObservableObject
{
    public ObservableCollection<DeviceInfo> Devices { get; } = new();

    [ObservableProperty]
    private DeviceInfo? _selectedDevice;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConnected;

    public bool CanSelectDevice => !IsConnected && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefreshDevices))]
    private async Task RefreshDevicesAsync()
    {
        IsBusy = true;
        RefreshDevicesCommand.NotifyCanExecuteChanged();
        try
        {
            var list = await Task.Run(() => DeviceManager.EnumerateAllDevices().ToList());
            Devices.Clear();
            foreach (var device in list)
            {
                Devices.Add(device);
            }

            if (SelectedDevice == null || !Devices.Contains(SelectedDevice))
            {
                SelectedDevice = Devices.FirstOrDefault();
            }
        }
        catch (Exception)
        {
            Devices.Clear();
            SelectedDevice = null;
        }
        finally
        {
            IsBusy = false;
            RefreshDevicesCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanSelectDevice));
        }
    }

    private bool CanRefreshDevices() => !IsConnected && !IsBusy;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectDevice));
        RefreshDevicesCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectDevice));
        RefreshDevicesCommand.NotifyCanExecuteChanged();
    }
}
