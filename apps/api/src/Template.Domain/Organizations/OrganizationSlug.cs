using System.Text;

namespace Template.Domain.Organizations;

public readonly record struct OrganizationSlug
{
    private const int MaximumGeneratedBaseLength = 48;
    private const int MaximumLength = 64;

    private OrganizationSlug(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? value, out OrganizationSlug slug)
    {
        slug = default;

        if (value is null)
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();

        if (!IsCanonical(normalized) || IsUuidShaped(normalized))
        {
            return false;
        }

        slug = new OrganizationSlug(normalized);
        return true;
    }

    public static string GenerateBase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var builder = new StringBuilder(Math.Min(name.Length, MaximumGeneratedBaseLength));
        var needsSeparator = false;

        foreach (var character in name)
        {
            if (IsAsciiLetterOrDigit(character))
            {
                if (needsSeparator && builder.Length > 0)
                {
                    if (builder.Length >= MaximumGeneratedBaseLength - 1)
                    {
                        break;
                    }

                    builder.Append('-');
                }

                if (builder.Length == MaximumGeneratedBaseLength)
                {
                    break;
                }

                builder.Append(char.ToLowerInvariant(character));
                needsSeparator = false;
                continue;
            }

            needsSeparator = builder.Length > 0;
        }

        var generated = builder.Length == 0 ? "workspace" : builder.ToString();
        return IsUuidShaped(generated)
            ? $"workspace-{generated}"
            : generated;
    }

    public override string ToString() => Value;

    private static bool IsCanonical(string value)
    {
        if (value.Length is 0 or > MaximumLength
            || value[0] == '-'
            || value[^1] == '-')
        {
            return false;
        }

        var previousWasHyphen = false;

        foreach (var character in value)
        {
            if (IsAsciiLetterOrDigit(character))
            {
                previousWasHyphen = false;
                continue;
            }

            if (character != '-' || previousWasHyphen)
            {
                return false;
            }

            previousWasHyphen = true;
        }

        return true;
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool IsUuidShaped(string value) =>
        Guid.TryParseExact(value, "D", out _);
}
