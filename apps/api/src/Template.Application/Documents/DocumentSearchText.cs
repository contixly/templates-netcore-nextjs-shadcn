using System.Globalization;
using System.Text;

namespace Template.Application.Documents;

public static class DocumentSearchText
{
    private const string EnglishKeyboardLayout = "qwertyuiop[]asdfghjkl;'zxcvbnm,.";
    private const string RussianKeyboardLayout = "йцукенгшщзхъфывапролджэячсмитьбю";

    private static readonly IReadOnlyDictionary<char, char> EnglishToRussianKeyboard =
        CreateKeyboardLayoutMap(EnglishKeyboardLayout, RussianKeyboardLayout);

    private static readonly IReadOnlyDictionary<char, char> RussianToEnglishKeyboard =
        CreateKeyboardLayoutMap(RussianKeyboardLayout, EnglishKeyboardLayout);

    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var result = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var originalRune in value.EnumerateRunes())
        {
            var rune = Rune.ToLowerInvariant(originalRune);

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

        var normalizedQuery = Normalize(query);

        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        var variants = new List<string> { normalizedQuery };
        var convertedQuery = ConvertKeyboardLayout(query);

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

        foreach (var originalRune in query.EnumerateRunes())
        {
            var rune = Rune.ToLowerInvariant(originalRune);

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

        var converted = new StringBuilder(query.Length);

        foreach (var originalRune in query.EnumerateRunes())
        {
            var rune = Rune.ToLowerInvariant(originalRune);

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
