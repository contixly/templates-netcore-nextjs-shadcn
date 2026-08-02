namespace Template.Domain.ApiKeys;

public static class ApiKeyScopes
{
    public const string BasicRead = "basic:read";
    public const string OrganizationRead = "organization:read";
    public const string MemberRead = "member:read";
    public const string TeamRead = "team:read";
    public const string TeamMemberRead = "teamMember:read";
}

public static class ApiKeyPolicy
{
    public const int MaximumNameLength = 32;
    public const int MaximumRateLimit = 1_000_000;

    private static readonly IReadOnlyDictionary<string, string[]> PresetScopes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["basic-read"] = [ApiKeyScopes.BasicRead],
            ["organization-read"] = [ApiKeyScopes.OrganizationRead],
            ["organization-members-read"] = [ApiKeyScopes.OrganizationRead, ApiKeyScopes.MemberRead],
            ["organization-teams-read"] = [ApiKeyScopes.OrganizationRead, ApiKeyScopes.TeamRead],
            ["organization-team-members-read"] = [ApiKeyScopes.OrganizationRead, ApiKeyScopes.TeamRead, ApiKeyScopes.TeamMemberRead],
            ["organization-read-all"] = [ApiKeyScopes.OrganizationRead, ApiKeyScopes.MemberRead, ApiKeyScopes.TeamRead, ApiKeyScopes.TeamMemberRead]
        };

    private static readonly IReadOnlyDictionary<string, TimeSpan?> Expirations =
        new Dictionary<string, TimeSpan?>(StringComparer.Ordinal)
        {
            ["never"] = null,
            ["7d"] = TimeSpan.FromDays(7),
            ["30d"] = TimeSpan.FromDays(30),
            ["90d"] = TimeSpan.FromDays(90),
            ["365d"] = TimeSpan.FromDays(365)
        };

    private static readonly IReadOnlyDictionary<string, TimeSpan> RateLimitWindows =
        new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
        {
            ["1m"] = TimeSpan.FromMinutes(1),
            ["1h"] = TimeSpan.FromHours(1),
            ["1d"] = TimeSpan.FromDays(1)
        };

    public static bool TryNormalizeName(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null)
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.EnumerateRunes().Count() > MaximumNameLength || trimmed.Any(char.IsControl))
        {
            return false;
        }

        normalized = trimmed;
        return true;
    }

    public static IReadOnlyList<string> ExpandPresets(IReadOnlyList<string>? presetIds)
    {
        if (presetIds is null || presetIds.Count == 0)
        {
            return [];
        }

        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var presetId in presetIds)
        {
            if (!PresetScopes.TryGetValue(presetId, out var presetScopes))
            {
                return [];
            }

            scopes.UnionWith(presetScopes);
        }

        return new string[]
        {
            ApiKeyScopes.BasicRead,
            ApiKeyScopes.OrganizationRead,
            ApiKeyScopes.MemberRead,
            ApiKeyScopes.TeamRead,
            ApiKeyScopes.TeamMemberRead
        }.Where(scopes.Contains).ToArray();
    }

    public static bool AreValidPresets(IReadOnlyList<string>? presetIds) =>
        presetIds is { Count: > 0 } && presetIds.All(PresetScopes.ContainsKey);

    public static bool TryGetExpiration(string? value, out TimeSpan? expiration) =>
        Expirations.TryGetValue(value ?? string.Empty, out expiration);

    public static bool TryGetRateLimitWindow(string? value, out TimeSpan window) =>
        RateLimitWindows.TryGetValue(value ?? string.Empty, out window);

    public static bool IsValidRateLimitMax(int value) => value is >= 1 and <= MaximumRateLimit;
}
