using Avalonia.Controls;
using NefMotoECUFlasher.Avalonia.ViewModels;

namespace NefMotoECUFlasher.Avalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = new AboutViewModel(this);
    }
}
