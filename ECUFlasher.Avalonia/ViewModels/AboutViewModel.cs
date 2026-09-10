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
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NefMotoECUFlasher.Avalonia.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly Window _window;

    public AboutViewModel(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        FullVersion = version != null
            ? $"Version {version.Major}.{version.Minor}.{version.Build}"
            : "Version 1.0.0";
    }

    public string FullVersion { get; }

    [RelayCommand]
    private void OpenReleases()
        => OpenUrl("https://github.com/NefariousMotorsports/NefMotoOpenSource/releases");

    [RelayCommand]
    private void OpenWebsite()
        => OpenUrl("https://nefariousmotorsports.com");

    [RelayCommand]
    private void Close() => _window.Close();

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore if browser cannot be launched
        }
    }
}
