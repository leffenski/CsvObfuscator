using System.Globalization;
using CsvObfuscator.Models;

namespace CsvObfuscator.Services;

public sealed class Obfuscator
{
    const string Vowels = "aeiouy";
    const string Consonants = "bcdfghjklmnpqrstvwxz";

    readonly Random _random = new();
    readonly Dictionary<(ObfuscationType Type, string Value), string> _cache = [];

    public string Transform(ObfuscationType type, string input)
    {
        if (type == ObfuscationType.Clear || input.Length == 0) return input;
        var key = (type, input);
        if (_cache.TryGetValue(key, out string? prior)) return prior;

        string result = type switch
        {
            ObfuscationType.Dob => ObfuscateDate(input),
            ObfuscationType.Name => ObfuscateCharacters(input, false),
            ObfuscationType.Ssn or ObfuscationType.Phone or ObfuscationType.Address => ObfuscateCharacters(input, true),
            _ => input
        };
        _cache[key] = result;
        return result;
    }

    string ObfuscateDate(string input)
    {
        if (!DateTime.TryParseExact(input, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return ObfuscateCharacters(input, true);

        int years, months, days;
        do
        {
            years = _random.Next(-5, 6);
            months = _random.Next(-2, 3);
            days = _random.Next(-10, 11);
        } while (years == 0 && months == 0 && days == 0);

        return date.AddYears(years).AddMonths(months).AddDays(days).ToString("M/d/yyyy", CultureInfo.InvariantCulture);
    }

    string ObfuscateCharacters(string input, bool replaceDigits)
    {
        char[] chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                char lower = char.ToLowerInvariant(c);
                string alphabet = Vowels.Contains(lower) ? Vowels : Consonants;
                int originalIndex = alphabet.IndexOf(lower);
                int replacementIndex = _random.Next(alphabet.Length - 1);
                if (originalIndex >= 0 && replacementIndex >= originalIndex) replacementIndex++;
                char replacement = alphabet[replacementIndex];
                chars[i] = char.IsUpper(c) ? char.ToUpperInvariant(replacement) : replacement;
            }
            else if (replaceDigits && c is >= '0' and <= '9')
            {
                int original = c - '0';
                int replacement = _random.Next(9);
                chars[i] = (char)('0' + (replacement >= original ? replacement + 1 : replacement));
            }
        }
        return new string(chars);
    }
}
