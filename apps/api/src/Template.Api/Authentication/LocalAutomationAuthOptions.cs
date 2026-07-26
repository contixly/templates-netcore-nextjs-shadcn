namespace Template.Api.Authentication;

internal sealed class LocalAutomationAuthOptions
{
    internal const string SectionName = "LocalAutomationAuth";

    public bool Enabled { get; init; }
    public int CreateRateLimitPerMinute { get; init; } = 20;
    public int SignInRateLimitPerFiveMinutes { get; init; } = 10;
}
