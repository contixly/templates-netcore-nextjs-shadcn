using System.Text;

namespace Template.Application.Organizations;

public static class OrganizationNamePolicy
{
    public const int MaximumLength = 50;

    public static bool TryNormalize(string? value, out string normalizedName)
    {
        normalizedName = value?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > MaximumLength)
        {
            return false;
        }

        foreach (var rune in normalizedName.EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune) &&
                rune.Value is not ' ' and not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }
}
