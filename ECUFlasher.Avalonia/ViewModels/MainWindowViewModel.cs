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
using Communication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared;

namespace NefMotoECUFlasher.Avalonia.ViewModels;

/// <summary>
/// Main window VM: protocol list, status bar placeholders, Exit and About.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private CommunicationInterface.Protocol _selectedProtocol = CommunicationInterface.Protocol.KWP2000;

    [ObservableProperty]
    private string _connectionStatusText = "No Connection";

    [ObservableProperty]
    private string _operationStatusText = "No Operation in Progress";

    [ObservableProperty]
    private double _percentOperationComplete;

    [ObservableProperty]
    private string _settingsTabHeader = "KWP2000 Settings";

    public IReadOnlyList<CommunicationInterface.Protocol> AvailableProtocols { get; } =
        [CommunicationInterface.Protocol.BootMode, CommunicationInterface.Protocol.KWP2000];

    partial void OnSelectedProtocolChanged(CommunicationInterface.Protocol value)
    {
        var desc = DescriptionAttributeConverterLogic.GetDescriptionAttribute(value) as string ?? value.ToString();
        SettingsTabHeader = $"{desc} Settings";
    }

    [RelayCommand]
    private void Exit() => OnExit?.Invoke();

    [RelayCommand]
    private void ShowAbout() => OnShowAbout?.Invoke();

    public Action? OnExit { get; set; }
    public Action? OnShowAbout { get; set; }
}
