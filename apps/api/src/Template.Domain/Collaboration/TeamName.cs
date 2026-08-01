using System.Text;

namespace Template.Domain.Collaboration;

public readonly record struct TeamName
{
    public const int MaximumLength = 50;

    public string Value { get; }

    private TeamName(string value) => Value = value;

    public static bool TryCreate(string? value, out TeamName name)
    {
        name = default;
        var normalized = value?.Trim();
        if (normalized is null or { Length: < 1 })
        {
            return false;
        }

        var scalarCount = 0;
        foreach (var rune in normalized.EnumerateRunes())
        {
            scalarCount++;
            if (scalarCount > MaximumLength ||
                !Rune.IsLetterOrDigit(rune) &&
                rune.Value is not ' ' and not '-' and not '_')
            {
                return false;
            }
        }

        name = new TeamName(normalized);
        return true;
    }

    public override string ToString() => Value;
}
