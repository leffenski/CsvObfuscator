using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CsvObfuscator.Models;
using CsvObfuscator.Services;

namespace CsvObfuscator.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    CsvDocument? _document;
    CsvDocument? _obfuscatedDocument;
    string? _obfuscatedText;
    Encoding _encoding = new UTF8Encoding(false);
    bool _hasBom;
    string _sourceName = "";

    public ObservableCollection<ColumnViewModel> Columns { get; } = [];
    public ObservableCollection<RowViewModel> PreviewRows { get; } = [];

    [ObservableProperty]
    public partial bool HasDocument { get; set; }

    [ObservableProperty]
    public partial bool HasObfuscatedOutput { get; set; }

    [ObservableProperty]
    public partial bool IsObfuscatedView { get; set; }

    [ObservableProperty]
    public partial string FileDescription { get; set; } = "Open a CSV file to begin.";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "No data is loaded.";

    public string SuggestedOutputName => string.IsNullOrEmpty(_sourceName)
        ? "data_obfuscated.csv"
        : $"{Path.GetFileNameWithoutExtension(_sourceName)}_obfuscated{Path.GetExtension(_sourceName)}";

    public async Task LoadAsync(Stream stream, string name)
    {
        try
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            byte[] bytes = memory.ToArray();
            string text = Decode(bytes, out _encoding, out _hasBom);
            _document = CsvDocument.Parse(text);
            _obfuscatedDocument = null;
            _obfuscatedText = null;
            HasObfuscatedOutput = false;
            IsObfuscatedView = false;
            _sourceName = name;

            Columns.Clear(); PreviewRows.Clear();
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
                var column = new ColumnViewModel(index, string.IsNullOrWhiteSpace(headers[index].Value) ? $"Column {index + 1}" : headers[index].Value);
                column.FilterChanged += RefreshPreview;
                column.TreatmentChanged += InvalidateObfuscatedOutput;
                Columns.Add(column);
            }
            HasDocument = true;
            FileDescription = $"{name} · {_document.Records.Count - 1:N0} data rows · {headers.Count:N0} columns";
            StatusMessage = "Set column treatments, optionally filter the preview, then save the obfuscated file.";
            RefreshPreview();
            OnPropertyChanged(nameof(SuggestedOutputName));
        }
        catch (Exception exception)
        {
            HasDocument = false;
            StatusMessage = $"Could not open this CSV: {exception.Message}";
        }
    }

    public async Task WriteObfuscatedAsync(Stream target)
    {
        if (_document is null) return;
        try
        {
            if (_obfuscatedText is null)
            {
                var obfuscator = new Obfuscator();
                _obfuscatedText = _document.Render((row, column, value) =>
                    row == 0 || column >= Columns.Count ? value : obfuscator.Transform(Columns[column].SelectedType, value));
                _obfuscatedDocument = CsvDocument.Parse(_obfuscatedText);
                HasObfuscatedOutput = true;
                IsObfuscatedView = true;
            }
            byte[] payload = _encoding.GetBytes(_obfuscatedText);
            if (_hasBom)
            {
                byte[] preamble = _encoding.GetPreamble();
                await target.WriteAsync(preamble);
            }
            await target.WriteAsync(payload);
            await target.FlushAsync();
            StatusMessage = "Obfuscated CSV saved successfully. Use the Obfuscated view switch to compare it with the clear-text input.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not save the obfuscated CSV: {exception.Message}";
        }
    }

    void RefreshPreview()
    {
        if (_document is null || _document.Records.Count == 0) return;
        PreviewRows.Clear();
        var previewDocument = IsObfuscatedView && _obfuscatedDocument is not null ? _obfuscatedDocument : _document;
        foreach (var record in previewDocument.Records.Skip(1))
        {
            bool matches = Columns.All(column =>
            {
                string value = column.Index < record.Fields.Count ? record.Fields[column.Index].Value : "";
                return string.IsNullOrEmpty(column.Filter) || value.Contains(column.Filter, StringComparison.OrdinalIgnoreCase);
            });
            if (matches)
                PreviewRows.Add(new RowViewModel([
                    .. record.Fields.Select((field, index) =>
                        new CellViewModel(field.Value, Columns[Math.Min(index, Columns.Count - 1)]))
                ]));
        }
        StatusMessage = $"Showing {PreviewRows.Count:N0} of {previewDocument.Records.Count - 1:N0} data rows in {(IsObfuscatedView ? "obfuscated" : "clear-text")} view. Filters affect preview only.";
    }

    partial void OnIsObfuscatedViewChanged(bool value) => RefreshPreview();

    void InvalidateObfuscatedOutput()
    {
        if (!HasObfuscatedOutput) return;
        _obfuscatedText = null;
        _obfuscatedDocument = null;
        HasObfuscatedOutput = false;
        IsObfuscatedView = false;
        StatusMessage = "A column treatment changed. Save again to generate a new obfuscated view.";
    }

    static string Decode(byte[] bytes, out Encoding encoding, out bool hasBom)
    {
        hasBom = false;
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)) { encoding = new UTF8Encoding(true); hasBom = true; return encoding.GetString(bytes[3..]); }
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble)) { encoding = Encoding.Unicode; hasBom = true; return encoding.GetString(bytes[2..]); }
        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble)) { encoding = Encoding.BigEndianUnicode; hasBom = true; return encoding.GetString(bytes[2..]); }
        encoding = new UTF8Encoding(false, true);
        try { return encoding.GetString(bytes); }
        catch (DecoderFallbackException) { encoding = Encoding.Latin1; return encoding.GetString(bytes); }
    }
}

public partial class ColumnViewModel : ObservableObject
{
    public static IReadOnlyList<ObfuscationType> AvailableTypes { get; } = Enum.GetValues<ObfuscationType>();
    public int Index { get; }
    public string Header { get; }
    public event Action? FilterChanged;
    public event Action? TreatmentChanged;

    [ObservableProperty]
    public partial ObfuscationType SelectedType { get; set; } = ObfuscationType.Clear;

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial double Width { get; set; } = 190;

    public ColumnViewModel(int index, string header) => (Index, Header) = (index, header);
    partial void OnFilterChanged(string value) => FilterChanged?.Invoke();
    partial void OnSelectedTypeChanged(ObfuscationType value) => TreatmentChanged?.Invoke();
}

public sealed record RowViewModel(IReadOnlyList<CellViewModel> Cells);
public sealed record CellViewModel(string Value, ColumnViewModel Column);
