namespace Template.Domain.Accounts;

public sealed record VerifiedEmail(string Value, string NormalizedValue)
{
    private const int MaximumLength = 254;

    public static VerifiedEmail Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("Email cannot contain control characters.", nameof(value));
        }

        var trimmedValue = value.Trim();

        if (trimmedValue.Length is 0 or > MaximumLength)
        {
            throw new ArgumentException("Email must contain at most 254 characters.", nameof(value));
        }

        return new VerifiedEmail(trimmedValue, trimmedValue.ToUpperInvariant());
    }
}
