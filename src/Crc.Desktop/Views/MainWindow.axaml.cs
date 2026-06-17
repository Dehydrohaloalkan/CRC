using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Crc.Desktop.ViewModels;

namespace Crc.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void OnBrowseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Select file or directory",
            AllowMultiple = false,
        };

        // Try file first; if user cancels, offer folder picker.
        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0 && DataContext is MainViewModel vm)
        {
            vm.TargetPath = files[0].Path.LocalPath;
            return;
        }

        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select directory"
        });
        if (folder.Count > 0 && DataContext is MainViewModel vm2)
            vm2.TargetPath = folder[0].Path.LocalPath;
    }

    private async void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save results",
            SuggestedFileName = "results.txt",
            FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }]
        });

        if (file is not null && DataContext is MainViewModel vm)
            await vm.SaveCommand.ExecuteAsync(file.Path.LocalPath);
    }
}
