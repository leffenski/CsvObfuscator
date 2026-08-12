using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CsvObfuscator.Models;
using CsvObfuscator.Services;

namespace CsvObfuscator.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    CsvDocument? _document;
    Encoding _encoding = new UTF8Encoding(false);
    List<CsvRecord> _filteredRecords = [];
    bool _hasBom;
    CsvDocument? _obfuscatedDocument;
    string? _obfuscatedText;
    CancellationTokenSource? _operationCancellation;
    string _sourceName = "";

    public ObservableCollection<ColumnViewModel> Columns { get; } = [];
    public ObservableCollection<RowViewModel> PreviewRows { get; } = [];
    public IReadOnlyList<int> PageSizes { get; } = [100, 500, 1000];

    [ObservableProperty] public partial bool HasDocument { get; set; }

    [ObservableProperty] public partial bool HasObfuscatedOutput { get; set; }

    [ObservableProperty] public partial bool IsObfuscatedView { get; set; }

    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial int SelectedPageSize { get; set; } = 100;

    [ObservableProperty] public partial int CurrentPage { get; set; } = 1;

    [ObservableProperty] public partial string FileDescription { get; set; } = "Open a CSV file to begin.";

    [ObservableProperty] public partial string StatusMessage { get; set; } = "No data is loaded.";

    public string SuggestedOutputName => string.IsNullOrEmpty(_sourceName)
        ? "data_obfuscated.csv"
        : $"{Path.GetFileNameWithoutExtension(_sourceName)}_obfuscated{Path.GetExtension(_sourceName)}";

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(_filteredRecords.Count / (double)SelectedPageSize));
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
    public bool CanCancel => IsBusy;
    public bool CanInteract => !IsBusy;
    public bool CanObfuscate => HasDocument && CanInteract;
    public bool CanSave => HasDocument && CanInteract;
    public bool CanToggleView => HasObfuscatedOutput && CanInteract;
    public bool CanPreviousPage => HasPreviousPage && CanInteract;
    public bool CanNextPage => HasNextPage && CanInteract;

    public async Task LoadAsync(Stream stream, string name)
    {
        if (IsBusy) return;
        using var operation = BeginOperation();
        try
        {
            await Task.Delay(1);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            byte[] bytes = memory.ToArray();
            string text = Decode(bytes, out _encoding, out _hasBom);
            operation.Token.ThrowIfCancellationRequested();
            _document = CsvDocument.Parse(text);
            _obfuscatedDocument = null;
            _obfuscatedText = null;
            HasObfuscatedOutput = false;
            IsObfuscatedView = false;
            _sourceName = name;
            _filteredRecords = [];
            CurrentPage = 1;

            Columns.Clear();
            PreviewRows.Clear();
            if (_document.Records.Count == 0)
            {
                HasDocument = false;
                FileDescription = name;
                StatusMessage = "The CSV is empty.";
                return;
            }

            List<CsvField> headers = _document.Records[0].Fields;
            for (int index = 0; index < headers.Count; index++)
            {
                var column = new ColumnViewModel(index,
                    string.IsNullOrWhiteSpace(headers[index].Value) ? $"Column {index + 1}" : headers[index].Value);
                column.FilterChanged += RefreshPreview;
                column.TreatmentChanged += InvalidateObfuscatedOutput;
                Columns.Add(column);
            }

            HasDocument = true;
            FileDescription = $"{name} · {_document.Records.Count - 1:N0} data rows · {headers.Count:N0} columns";
            StatusMessage = "Set column treatments, optionally filter the preview, then obfuscate and save the file.";
            RefreshPreview();
            OnPropertyChanged(nameof(SuggestedOutputName));
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                StatusMessage = "Loading canceled.";
                return;
            }

            HasDocument = false;
            StatusMessage = $"Could not open this CSV: {exception.Message}";
        }
    }

    public async Task ObfuscateAsync()
    {
        if (_document is null) return;

        using var operation = BeginOperation();
        try
        {
            await Task.Delay(1);
            GenerateObfuscatedOutput(operation.Token);
            IsObfuscatedView = true;
            StatusMessage =
                "Obfuscation preview generated from the original data. Press Obfuscate again to generate a new result.";
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                StatusMessage = "Obfuscation canceled.";
                return;
            }

            StatusMessage = $"Could not obfuscate the CSV: {exception.Message}";
        }
    }

    public async Task WriteObfuscatedAsync(Stream target)
    {
        if (_document is null) return;
        using var operation = BeginOperation();
        try
        {
            await Task.Delay(1);
            // Save uses the preview, if one exists, and otherwise generates it lazily.
            if (_obfuscatedText is null)
                GenerateObfuscatedOutput(operation.Token);
            byte[] payload = _encoding.GetBytes(_obfuscatedText!);
            operation.Token.ThrowIfCancellationRequested();
            if (_hasBom)
            {
                byte[] preamble = _encoding.GetPreamble();
                await target.WriteAsync(preamble);
            }

            await target.WriteAsync(payload);
            await target.FlushAsync();
            StatusMessage = "Obfuscated CSV saved successfully.";
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                StatusMessage = "Save canceled.";
                return;
            }

            StatusMessage = $"Could not save the obfuscated CSV: {exception.Message}";
        }
    }

    public void CancelOperation()
    {
        _operationCancellation?.Cancel();
    }

    OperationScope BeginOperation()
    {
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        var cancellation = _operationCancellation;
        return new OperationScope(cancellation.Token, () => EndOperation(cancellation));
    }

    void EndOperation(CancellationTokenSource cancellation)
    {
        if (!ReferenceEquals(_operationCancellation, cancellation)) return;
        _operationCancellation = null;
        IsBusy = false;
    }

    void GenerateObfuscatedOutput(CancellationToken cancellationToken)
    {
        if (_document is null) return;

        var obfuscator = new Obfuscator();
        _obfuscatedText = _document.Render((row, column, value) =>
                row == 0 || column >= Columns.Count ? value : obfuscator.Transform(Columns[column].SelectedType, value),
            cancellationToken);
        _obfuscatedDocument = CsvDocument.Parse(_obfuscatedText);
        HasObfuscatedOutput = true;
    }

    void RefreshPreview()
    {
        if (_document is null || _document.Records.Count == 0) return;
        var previewDocument = IsObfuscatedView && _obfuscatedDocument is not null ? _obfuscatedDocument : _document;
        _filteredRecords = previewDocument.Records.Skip(1).Where(record => Columns.All(column =>
        {
            string value = column.Index < record.Fields.Count ? record.Fields[column.Index].Value : "";
            return string.IsNullOrEmpty(column.Filter) ||
                   value.Contains(column.Filter, StringComparison.OrdinalIgnoreCase);
        })).ToList();
        CurrentPage = Math.Min(CurrentPage, TotalPages);
        RefreshPage();
        StatusMessage =
            $"Showing page {CurrentPage:N0} of {TotalPages:N0} · {_filteredRecords.Count:N0} matching rows in {(IsObfuscatedView ? "obfuscated" : "clear-text")} view. Filters affect the entire dataset.";
    }

    void RefreshPage()
    {
        PreviewRows.Clear();
        foreach (var record in _filteredRecords.Skip((CurrentPage - 1) * SelectedPageSize).Take(SelectedPageSize))
            PreviewRows.Add(new RowViewModel([
                .. record.Fields.Select((field, index) =>
                    new CellViewModel(field.Value, Columns[Math.Min(index, Columns.Count - 1)]))
            ]));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(CanPreviousPage));
        OnPropertyChanged(nameof(CanNextPage));
    }

    public void PreviousPage()
    {
        if (HasPreviousPage)
        {
            CurrentPage--;
            RefreshPage();
        }
    }

    public void NextPage()
    {
        if (HasNextPage)
        {
            CurrentPage++;
            RefreshPage();
        }
    }

    partial void OnIsObfuscatedViewChanged(bool value)
    {
        RefreshPreview();
    }

    partial void OnSelectedPageSizeChanged(int value)
    {
        CurrentPage = 1;
        RefreshPage();
    }

    partial void OnCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(CanPreviousPage));
        OnPropertyChanged(nameof(CanNextPage));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanObfuscate));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanToggleView));
        OnPropertyChanged(nameof(CanPreviousPage));
        OnPropertyChanged(nameof(CanNextPage));
    }

    partial void OnHasDocumentChanged(bool value)
    {
        OnPropertyChanged(nameof(CanObfuscate));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnHasObfuscatedOutputChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleView));
    }

    void InvalidateObfuscatedOutput()
    {
        if (!HasObfuscatedOutput) return;
        _obfuscatedText = null;
        _obfuscatedDocument = null;
        HasObfuscatedOutput = false;
        IsObfuscatedView = false;
        StatusMessage = "A column treatment changed. Obfuscate again to generate a new preview.";
    }

    static string Decode(byte[] bytes, out Encoding encoding, out bool hasBom)
    {
        hasBom = false;
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            encoding = new UTF8Encoding(true);
            hasBom = true;
            return encoding.GetString(bytes[3..]);
        }

        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
        {
            encoding = Encoding.Unicode;
            hasBom = true;
            return encoding.GetString(bytes[2..]);
        }

        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            encoding = Encoding.BigEndianUnicode;
            hasBom = true;
            return encoding.GetString(bytes[2..]);
        }

        encoding = new UTF8Encoding(false, true);
        try
        {
            return encoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            encoding = Encoding.Latin1;
            return encoding.GetString(bytes);
        }
    }
}

internal sealed class OperationScope(CancellationToken token, Action onDispose) : IDisposable
{
    public CancellationToken Token => token;

    public void Dispose()
    {
        onDispose();
    }
}

public partial class ColumnViewModel : ObservableObject
{
    public ColumnViewModel(int index, string header)
    {
        (Index, Header) = (index, header);
    }

    public static IReadOnlyList<ObfuscationType> AvailableTypes { get; } = Enum.GetValues<ObfuscationType>();
    public int Index { get; }
    public string Header { get; }

    [ObservableProperty] public partial ObfuscationType SelectedType { get; set; } = ObfuscationType.Clear;

    [ObservableProperty] public partial string Filter { get; set; } = "";

    [ObservableProperty] public partial double Width { get; set; } = 190;
    public event Action? FilterChanged;
    public event Action? TreatmentChanged;

    partial void OnFilterChanged(string value)
    {
        FilterChanged?.Invoke();
    }

    partial void OnSelectedTypeChanged(ObfuscationType value)
    {
        TreatmentChanged?.Invoke();
    }
}

public sealed record RowViewModel(IReadOnlyList<CellViewModel> Cells);

public sealed record CellViewModel(string Value, ColumnViewModel Column);