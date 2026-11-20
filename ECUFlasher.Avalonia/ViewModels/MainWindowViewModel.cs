using System;
using System.Collections.ObjectModel;
using Communication;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NefMotoECUFlasher.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public DeviceSelectionViewModel Devices { get; } = new();

    public LayoutSelectionViewModel Layouts { get; } = new();

    public ActionStubViewModels Actions { get; }

    public ObservableCollection<CommunicationInterface.Protocol> Protocols { get; } = new()
    {
        CommunicationInterface.Protocol.KWP2000,
        CommunicationInterface.Protocol.BootMode,
    };

    [ObservableProperty]
    private CommunicationInterface.Protocol _selectedProtocol = CommunicationInterface.Protocol.KWP2000;

    [ObservableProperty]
    private string _logText = string.Empty;

    public MainWindowViewModel()
    {
        Actions = new ActionStubViewModels(AppendLog);
        AppendLog("ECUFlash Avalonia companion shell.");
        AppendLog("Device/layout refresh only; action commands are stubs (no CLI changes).");
        AppendLog("Feature wiring waits on #159 / #160 + shared ops on master.");
        Layouts.RefreshLayoutsCommand.Execute(null);
        _ = Devices.RefreshDevicesCommand.ExecuteAsync(null);
    }

    public void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        LogText = string.IsNullOrEmpty(LogText)
            ? $"[{stamp}] {line}"
            : $"{LogText}{Environment.NewLine}[{stamp}] {line}";
    }
}
