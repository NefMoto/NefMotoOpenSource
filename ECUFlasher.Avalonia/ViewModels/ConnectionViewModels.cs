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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Communication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NefMotoECUFlasher.Avalonia.ViewModels;

public enum KwpConnectionMethod : uint
{
    [Description("Slow Init")]
    SlowInit = 0,
    [Description("Fast Init")]
    FastInit
}

/// <summary>
/// KWP2000 connection strip (baud, device, method, connect). Connect is UI-stub until protocol VMs are ported.
/// </summary>
public partial class Kwp2000ConnectionViewModel : ObservableObject
{
    public Kwp2000ConnectionViewModel(DeviceSelectionViewModel devices, Action<string>? setConnectionStatus = null)
    {
        Devices = devices;
        _setConnectionStatus = setConnectionStatus;
        Devices.PropertyChanged += OnDevicesPropertyChanged;
        AvailableBaudRates = new ObservableCollection<uint>(
            Enum.GetValues<KWP2000BaudRates>()
                .Cast<KWP2000BaudRates>()
                .Select(b => (uint)b)
                .Where(b => b != 0)
                .Distinct());
        DesiredBaudRate = AvailableBaudRates.Contains((uint)KWP2000BaudRates.BAUD_DEFAULT)
            ? (uint)KWP2000BaudRates.BAUD_DEFAULT
            : AvailableBaudRates.Last();
        DesiredConnectionMethod = KwpConnectionMethod.SlowInit;
    }

    private readonly Action<string>? _setConnectionStatus;

    public DeviceSelectionViewModel Devices { get; }

    public ObservableCollection<uint> AvailableBaudRates { get; }

    public IReadOnlyList<KwpConnectionMethod> AvailableConnectionMethods { get; } =
        [KwpConnectionMethod.SlowInit, KwpConnectionMethod.FastInit];

    [ObservableProperty]
    private uint _desiredBaudRate;

    [ObservableProperty]
    private KwpConnectionMethod _desiredConnectionMethod;

    public string ConnectButtonText => Devices.IsConnected ? "Disconnect" : "Connect";

    public bool CanChangeConnectionOptions => !Devices.IsConnected;

    [RelayCommand]
    private void ToggleConnect()
    {
        if (Devices.IsConnected)
        {
            Devices.IsConnected = false;
            _setConnectionStatus?.Invoke("No Connection");
        }
        else
        {
            // Full KWP2000Interface connect still lives in WPF VMs; stub status for UI workalike.
            Devices.IsConnected = true;
            var method = DesiredConnectionMethod == KwpConnectionMethod.FastInit ? "Fast Init" : "Slow Init";
            var device = Devices.SelectedDevice?.DisplayName ?? "no device";
            _setConnectionStatus?.Invoke($"Connected (UI stub): {method}, {DesiredBaudRate} baud, {device}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelOperation()
    {
        // Placeholder until operation pipeline is ported.
    }

    private bool CanCancel() => Devices.IsConnected;

    private void OnDevicesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceSelectionViewModel.IsConnected)
            or nameof(DeviceSelectionViewModel.CanSelectDevice))
        {
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(CanChangeConnectionOptions));
            CancelOperationCommand.NotifyCanExecuteChanged();
        }
    }
}

/// <summary>
/// Boot Mode connection strip (baud, device, connect). Connect is UI-stub until protocol VMs are ported.
/// </summary>
public partial class BootstrapConnectionViewModel : ObservableObject
{
    public BootstrapConnectionViewModel(DeviceSelectionViewModel devices, Action<string>? setConnectionStatus = null)
    {
        Devices = devices;
        _setConnectionStatus = setConnectionStatus;
        Devices.PropertyChanged += OnDevicesPropertyChanged;
        AvailableBaudRates = new ObservableCollection<uint> { 9600, 19200, 38400, 57600, 124800 };
        DesiredBaudRate = 57600;
    }

    private readonly Action<string>? _setConnectionStatus;

    public DeviceSelectionViewModel Devices { get; }

    public ObservableCollection<uint> AvailableBaudRates { get; }

    [ObservableProperty]
    private uint _desiredBaudRate;

    public string ConnectButtonText => Devices.IsConnected ? "Disconnect" : "Connect";

    public bool CanChangeConnectionOptions => !Devices.IsConnected;

    [RelayCommand]
    private void ToggleConnect()
    {
        if (Devices.IsConnected)
        {
            Devices.IsConnected = false;
            _setConnectionStatus?.Invoke("No Connection");
        }
        else
        {
            Devices.IsConnected = true;
            var device = Devices.SelectedDevice?.DisplayName ?? "no device";
            _setConnectionStatus?.Invoke($"Connected (UI stub): Boot Mode, {DesiredBaudRate} baud, {device}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelOperation()
    {
    }

    private bool CanCancel() => Devices.IsConnected;

    private void OnDevicesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceSelectionViewModel.IsConnected)
            or nameof(DeviceSelectionViewModel.CanSelectDevice))
        {
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(CanChangeConnectionOptions));
            CancelOperationCommand.NotifyCanExecuteChanged();
        }
    }
}
