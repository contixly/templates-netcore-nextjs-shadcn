namespace Template.Application.Authentication;

public static class LocalAutomationCredentialPolicy
{
    public const string EmailPrefix = "local-agent+";
    public const string EmailDomain = "local-agent.test";
    public const string CleanupPath = "/api/local-auth/scenario";
    public const int GeneratedCollisionAttempts = 3;

    public static string NormalizeName(string value) => value.Trim();

    public static string NormalizeEmail(string value) =>
        value.Trim().ToLowerInvariant();

    public static bool IsLocalEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeEmail(value);
        var suffix = $"@{EmailDomain}";
        if (!normalized.StartsWith(EmailPrefix, StringComparison.Ordinal) ||
            !normalized.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var discriminator = normalized[
            EmailPrefix.Length..^suffix.Length];
        return discriminator.Length > 0 &&
               !discriminator.Contains('@');
    }
}
