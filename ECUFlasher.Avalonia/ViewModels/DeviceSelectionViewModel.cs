/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2017  Nefarious Motorsports Inc

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Communication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NefMotoECUFlasher.Avalonia.ViewModels;

/// <summary>
/// Shared device list + selection for connection panels (WPF App.Devices equivalent).
/// </summary>
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
            // Unexpected enumeration errors leave the list empty. Linux has no cable backend.
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
