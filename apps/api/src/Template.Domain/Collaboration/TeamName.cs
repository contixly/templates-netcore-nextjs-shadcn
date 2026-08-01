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
        if (normalized is null or { Length: < 1 or > MaximumLength } ||
            normalized.Any(char.IsControl) ||
            normalized.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not ' ' and not '-' and not '_'))
        {
            return false;
        }

        name = new TeamName(normalized);
        return true;
    }

    public override string ToString() => Value;
}
