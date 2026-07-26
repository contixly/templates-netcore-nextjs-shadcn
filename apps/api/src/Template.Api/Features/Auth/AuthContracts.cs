using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Template.Api.Features.Auth;

internal sealed record AuthProviderResponse(string Id, string DisplayName);

internal sealed record AuthCapabilitiesResponse(
    bool LocalAutomationEnabled,
    IReadOnlyList<AuthProviderResponse> Providers);

internal sealed record AuthUserResponse(
    Guid Id,
    string Name,
    string Email,
    bool EmailVerified,
    string? Image);

internal sealed record AuthSessionMetadataResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt);

internal sealed record AuthSessionResponse(
    bool Authenticated,
    AuthUserResponse? User,
    AuthSessionMetadataResponse? Session);

internal sealed record AuthCsrfResponse(string RequestToken);

internal sealed record LocalAutomationScenarioResponse(
    AuthUserResponse User,
    string Email,
    string Password,
    string CleanupUrl);

internal sealed record LocalAutomationCleanupResponse(int DeletedOrganizations);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CreateLocalAutomationScenarioRequest
{
    public string? Name { get; init; }

    public string? Email { get; init; }

    [StringLength(128, MinimumLength = 12)]
    public string? Password { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LocalAutomationSignInRequest
{
    [Required]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;
}
