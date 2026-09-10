using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NefMotoECUFlasher.Avalonia.ViewModels;
using NefMotoECUFlasher.Avalonia.Views;

namespace NefMotoECUFlasher.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        try
        {
            // Prefer PNG on macOS; ICO works for Windows title bar / ApplicationIcon
            var iconUri = OperatingSystem.IsMacOS()
                ? "avares://NefMotoECUFlasher.Avalonia/Assets/ProgramIcon.png"
                : "avares://NefMotoECUFlasher.Avalonia/Assets/ProgramIcon.ico";
            using var stream = AssetLoader.Open(new System.Uri(iconUri));
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // Icon is best-effort (macOS Dock often needs a .app bundle + .icns)
        }

        var vm = new MainWindowViewModel();
        vm.OnExit = Close;
        vm.OnShowAbout = () => new AboutWindow().ShowDialog(this);
        DataContext = vm;
    }
}
