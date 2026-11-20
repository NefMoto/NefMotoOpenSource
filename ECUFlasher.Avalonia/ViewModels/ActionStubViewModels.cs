using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NefMotoECUFlasher.Avalonia.ViewModels;

/// <summary>
/// Placeholder action commands. Real work waits on shared ops / #159 / #160; no protocol I/O here.
/// </summary>
public partial class ActionStubViewModels : ObservableObject
{
    private readonly Action<string> _log;

    public ActionStubViewModels(Action<string> log)
    {
        _log = log;
    }

    [RelayCommand]
    private void Info() =>
        _log("Info: stub (blocked until shared flash/info ops).");

    [RelayCommand]
    private void Flash() =>
        _log("Flash: stub (blocked until shared flash ops).");

    [RelayCommand]
    private void Verify() =>
        _log("Verify: stub (blocked until #159 bootmode verify).");

    [RelayCommand]
    private void Dtc() =>
        _log("DTC: stub (blocked until #160 CLI helpers).");

    [RelayCommand]
    private void Immo() =>
        _log("Immo / EEPROM: stub (blocked until #160 CLI helpers).");
}
