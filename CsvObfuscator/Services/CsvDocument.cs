using System.Text;

namespace CsvObfuscator.Services;

/// <summary>RFC 4180-style CSV document that retains each field's original quoting and line ending.</summary>
public sealed class CsvDocument
{
    public required char Delimiter { get; init; }
    public required List<CsvRecord> Records { get; init; }

    public static CsvDocument Parse(string text)
    {
        char delimiter = DetectDelimiter(text);
        var records = new List<CsvRecord>();
        var fields = new List<CsvField>();
        var value = new StringBuilder();
        var raw = new StringBuilder();
        bool quoted = false;
        bool inQuotes = false;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    raw.Append(c);
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        raw.Append('"');
                        value.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                raw.Append(c);
                value.Append(c);
                i++;
                continue;
            }

            if (c == '"' && raw.Length == 0)
            {
                quoted = true;
                inQuotes = true;
                raw.Append(c);
                i++;
                continue;
            }

            if (c == delimiter)
            {
                AddField();
                i++;
                continue;
            }

            if (c is '\r' or '\n')
            {
                string ending = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : c.ToString();
                AddRecord(ending);
                i += ending.Length;
                continue;
            }

            raw.Append(c);
            value.Append(c);
            i++;
        }

        if (fields.Count > 0 || raw.Length > 0 || value.Length > 0 || (text.Length > 0 && text[^1] == delimiter))
            AddRecord(string.Empty);

        return inQuotes
            ? throw new FormatException("The selected file contains an unterminated quoted CSV field.")
            : new CsvDocument { Delimiter = delimiter, Records = records };

        void AddRecord(string lineEnding)
        {
            AddField();
            records.Add(new CsvRecord(fields, lineEnding));
            fields = [];
        }

        void AddField()
        {
            fields.Add(new CsvField(value.ToString(), raw.ToString(), quoted));
            value.Clear();
            raw.Clear();
            quoted = false;
        }
    }

    public string Render(Func<int, int, string, string> transform, CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        for (int rowIndex = 0; rowIndex < Records.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = Records[rowIndex];
            for (int columnIndex = 0; columnIndex < record.Fields.Count; columnIndex++)
            {
                if (columnIndex > 0) output.Append(Delimiter);
                var field = record.Fields[columnIndex];
                string transformed = transform(rowIndex, columnIndex, field.Value);
                output.Append(field.Quoted ? '"' + transformed.Replace("\"", "\"\"") + '"' : transformed);
            }

            output.Append(record.LineEnding);
        }

        return output.ToString();
    }

    static char DetectDelimiter(string text)
    {
        char[] candidates = [',', ';', '\t', '|'];
        Dictionary<char, int> counts = candidates.ToDictionary(x => x, _ => 0);
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                quoted = !quoted;
                continue;
            }

            if (!quoted && (text[i] == '\r' || text[i] == '\n')) break;

            if (!quoted && counts.ContainsKey(text[i]))
                counts[text[i]]++;
        }

        return counts.OrderByDescending(x => x.Value).First().Key;
    }
}

public sealed record CsvRecord(List<CsvField> Fields, string LineEnding);

public sealed record CsvField(string Value, string Raw, bool Quoted);