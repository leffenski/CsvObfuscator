using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace CsvObfuscator.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    async void OpenCsv_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open CSV file",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV files") { Patterns = ["*.csv"] }]
        });

        if (files.Count == 0 || DataContext is not ViewModels.MainViewModel viewModel)
            return;

        await using var stream = await files[0].OpenReadAsync();
        await viewModel.LoadAsync(stream, files[0].Name);
    }

    async void SaveCsv_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel { HasDocument: true } viewModel)
            return;

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;

        if (storage is null)
            return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save obfuscated CSV",
            SuggestedFileName = viewModel.SuggestedOutputName,
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV files") { Patterns = ["*.csv"] }]
        });

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await viewModel.WriteObfuscatedAsync(stream);
    }
}
