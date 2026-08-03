using System.Globalization;
using System.Text;

namespace Template.Application.Documents;

public static class DocumentSearchText
{
    private const string EnglishKeyboardLayout = "`qwertyuiop[]asdfghjkl;'zxcvbnm,.";
    private const string RussianKeyboardLayout = "ёйцукенгшщзхъфывапролджэячсмитьбю";

    private static readonly IReadOnlyDictionary<char, char> EnglishToRussianKeyboard =
        CreateKeyboardLayoutMap(EnglishKeyboardLayout, RussianKeyboardLayout);

    private static readonly IReadOnlyDictionary<char, char> RussianToEnglishKeyboard =
        CreateKeyboardLayoutMap(RussianKeyboardLayout, EnglishKeyboardLayout);

    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var result = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var lowerRune in ToUnicodeDefaultLower(
                     value.Normalize(NormalizationForm.FormC)).EnumerateRunes())
        {
            var rune = lowerRune;

            if (rune.Value == 'ё')
            {
                rune = new Rune('е');
            }

            if (IsLetterOrNumber(rune))
            {
                if (pendingSpace && result.Length > 0)
                {
                    result.Append(' ');
                }

                result.Append(rune.ToString());
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return result.ToString();
    }

    public static IReadOnlyList<string> CreateQueryVariants(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var canonicalQuery = query.Normalize(NormalizationForm.FormC);
        var normalizedQuery = Normalize(canonicalQuery);

        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        var variants = new List<string> { normalizedQuery };
        var convertedQuery = ConvertKeyboardLayout(canonicalQuery);

        if (convertedQuery is not null)
        {
            var normalizedConvertedQuery = Normalize(convertedQuery);

            if (normalizedConvertedQuery.Length > 0 && normalizedConvertedQuery != normalizedQuery)
            {
                variants.Add(normalizedConvertedQuery);
            }
        }

        return variants;
    }

    public static int GetAllowedTypoDistance(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.Length <= 3)
        {
            return 0;
        }

        return token.Length <= 7 ? 1 : 2;
    }

    public static int GetDamerauLevenshteinDistance(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var rows = left.Length + 1;
        var columns = right.Length + 1;
        var matrix = new int[rows, columns];

        for (var row = 0; row < rows; row++)
        {
            matrix[row, 0] = row;
        }

        for (var column = 0; column < columns; column++)
        {
            matrix[0, column] = column;
        }

        for (var row = 1; row < rows; row++)
        {
            for (var column = 1; column < columns; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                matrix[row, column] = Math.Min(
                    Math.Min(matrix[row - 1, column] + 1, matrix[row, column - 1] + 1),
                    matrix[row - 1, column - 1] + cost);

                if (row > 1 &&
                    column > 1 &&
                    left[row - 1] == right[column - 2] &&
                    left[row - 2] == right[column - 1])
                {
                    matrix[row, column] = Math.Min(matrix[row, column], matrix[row - 2, column - 2] + 1);
                }
            }
        }

        return matrix[left.Length, right.Length];
    }

    internal static IReadOnlyList<string> Tokenize(string value) =>
        Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static Dictionary<char, char> CreateKeyboardLayoutMap(string source, string target) =>
        source.Select((character, index) => new KeyValuePair<char, char>(character, target[index]))
            .ToDictionary();

    private static string? ConvertKeyboardLayout(string query)
    {
        IReadOnlyDictionary<char, char>? layoutMap = null;
        var hasLayoutLetter = false;
        var lowerQuery = ToUnicodeDefaultLower(query);

        foreach (var rune in lowerQuery.EnumerateRunes())
        {
            if (!IsLetter(rune))
            {
                continue;
            }

            if (!rune.IsAscii)
            {
                if (rune.Value > char.MaxValue || !RussianToEnglishKeyboard.ContainsKey((char)rune.Value))
                {
                    return null;
                }

                if (layoutMap is not null && !ReferenceEquals(layoutMap, RussianToEnglishKeyboard))
                {
                    return null;
                }

                layoutMap = RussianToEnglishKeyboard;
            }
            else
            {
                var character = (char)rune.Value;

                if (!EnglishToRussianKeyboard.ContainsKey(character))
                {
                    return null;
                }

                if (layoutMap is not null && !ReferenceEquals(layoutMap, EnglishToRussianKeyboard))
                {
                    return null;
                }

                layoutMap = EnglishToRussianKeyboard;
            }

            hasLayoutLetter = true;
        }

        if (!hasLayoutLetter || layoutMap is null)
        {
            return null;
        }

        var converted = new StringBuilder(lowerQuery.Length);

        foreach (var rune in lowerQuery.EnumerateRunes())
        {
            if (rune.Value <= char.MaxValue && layoutMap.TryGetValue((char)rune.Value, out var character))
            {
                converted.Append(character);
            }
            else
            {
                converted.Append(rune.ToString());
            }
        }

        return converted.ToString();
    }

    private static string ToUnicodeDefaultLower(string value)
    {
        var runes = value.EnumerateRunes().ToArray();
        var result = new StringBuilder(value.Length);

        for (var index = 0; index < runes.Length; index++)
        {
            var rune = runes[index];

            if (rune.Value == 0x0130)
            {
                result.Append("i\u0307");
                continue;
            }

            if (rune.Value == 0x03A3 && IsFinalSigma(runes, index))
            {
                result.Append('\u03c2');
                continue;
            }

            result.Append(Rune.ToLowerInvariant(rune).ToString());
        }

        return result.ToString();
    }

    private static bool IsFinalSigma(IReadOnlyList<Rune> runes, int sigmaIndex)
    {
        var precededByCased = false;

        for (var index = sigmaIndex - 1; index >= 0; index--)
        {
            if (IsCaseIgnorable(runes[index]))
            {
                continue;
            }

            precededByCased = IsCased(runes[index]);
            break;
        }

        if (!precededByCased)
        {
            return false;
        }

        for (var index = sigmaIndex + 1; index < runes.Count; index++)
        {
            if (IsCaseIgnorable(runes[index]))
            {
                continue;
            }

            return !IsCased(runes[index]);
        }

        return true;
    }

    private static bool IsCased(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);

        return category is
                UnicodeCategory.UppercaseLetter or
                UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter ||
            IsOtherCased(rune.Value) ||
            Rune.ToLowerInvariant(rune) != Rune.ToUpperInvariant(rune);
    }

    private static bool IsOtherCased(int value) => value is
        0x00AA or 0x00BA or
        >= 0x02B0 and <= 0x02B8 or
        >= 0x02C0 and <= 0x02C1 or
        >= 0x02E0 and <= 0x02E4 or
        0x0345 or 0x037A or 0x10FC or
        >= 0x1D2C and <= 0x1D6A or
        0x1D78 or
        >= 0x1D9B and <= 0x1DBF or
        0x2071 or 0x207F or
        >= 0x2090 and <= 0x209C or
        >= 0x2160 and <= 0x217F or
        >= 0x24B6 and <= 0x24E9 or
        >= 0x2C7C and <= 0x2C7D or
        >= 0xA69C and <= 0xA69D or
        0xA770 or
        >= 0xA7F2 and <= 0xA7F4 or
        >= 0xA7F8 and <= 0xA7F9 or
        >= 0xAB5C and <= 0xAB5F or
        0xAB69 or 0x10780 or
        >= 0x10783 and <= 0x10785 or
        >= 0x10787 and <= 0x107B0 or
        >= 0x107B2 and <= 0x107BA or
        >= 0x1E030 and <= 0x1E06D or
        >= 0x1F130 and <= 0x1F149 or
        >= 0x1F150 and <= 0x1F169 or
        >= 0x1F170 and <= 0x1F189;

    private static bool IsCaseIgnorable(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);

        return category is
                UnicodeCategory.NonSpacingMark or
                UnicodeCategory.EnclosingMark or
                UnicodeCategory.Format or
                UnicodeCategory.ModifierLetter or
                UnicodeCategory.ModifierSymbol ||
            rune.Value is
                0x0027 or 0x002E or 0x003A or 0x00B7 or 0x0387 or 0x055F or 0x05F4 or
                0x2018 or 0x2019 or 0x2024 or 0x2027 or 0xFE13 or 0xFE52 or
                0xFE55 or 0xFF07 or 0xFF0E or 0xFF1A;
    }

    private static bool IsLetterOrNumber(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);

        return IsLetterCategory(category) || category is
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.LetterNumber or
            UnicodeCategory.OtherNumber;
    }

    private static bool IsLetter(Rune rune) => IsLetterCategory(Rune.GetUnicodeCategory(rune));

    private static bool IsLetterCategory(UnicodeCategory category) => category is
        UnicodeCategory.UppercaseLetter or
        UnicodeCategory.LowercaseLetter or
        UnicodeCategory.TitlecaseLetter or
        UnicodeCategory.ModifierLetter or
        UnicodeCategory.OtherLetter;
}
