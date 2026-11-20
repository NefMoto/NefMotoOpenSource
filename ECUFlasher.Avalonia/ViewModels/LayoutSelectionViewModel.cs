using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared;

namespace NefMotoECUFlasher.Avalonia.ViewModels;

public partial class LayoutSelectionViewModel : ObservableObject
{
    public ObservableCollection<string> LayoutNames { get; } = new();

    [ObservableProperty]
    private string? _selectedLayoutName;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private void RefreshLayouts()
    {
        IsBusy = true;
        RefreshLayoutsCommand.NotifyCanExecuteChanged();
        try
        {
            LayoutNames.Clear();
            string? dir = MemoryLayout.GetLayoutsDirectory();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                SelectedLayoutName = null;
                return;
            }

            foreach (string path in Directory.GetFiles(dir, "*" + MemoryLayout.MEMORY_LAYOUT_FILE_EXT)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.GetFileName(path);
                string name = file.EndsWith(MemoryLayout.MEMORY_LAYOUT_FILE_EXT, StringComparison.OrdinalIgnoreCase)
                    ? file[..^MemoryLayout.MEMORY_LAYOUT_FILE_EXT.Length]
                    : Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(name))
                {
                    LayoutNames.Add(name);
                }
            }

            if (SelectedLayoutName == null || !LayoutNames.Contains(SelectedLayoutName))
            {
                SelectedLayoutName = LayoutNames.FirstOrDefault();
            }
        }
        catch
        {
            LayoutNames.Clear();
            SelectedLayoutName = null;
        }
        finally
        {
            IsBusy = false;
            RefreshLayoutsCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRefresh() => !IsBusy;
}
