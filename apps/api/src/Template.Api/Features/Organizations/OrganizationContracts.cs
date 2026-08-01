using System.Text.Json.Serialization;

namespace Template.Api.Features.Organizations;

internal static class OrganizationContractLimits
{
    internal const int MaximumAllowedEmailDomains = 100;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CreateOrganizationRequest
{
    public string? Name { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record UpdateOrganizationRequest
{
    public string? Name { get; init; }

    public string? Slug { get; init; }

    public IReadOnlyList<string>? AllowedEmailDomains { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DeleteOrganizationRequest
{
    public string? ConfirmationName { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SetActiveOrganizationRequest
{
    public Guid? OrganizationId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AddOrganizationMemberRequest
{
    public Guid? UserId { get; init; }

    public string? Role { get; init; }

    public bool? AcknowledgeDomainRestriction { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record UpdateOrganizationMemberRoleRequest
{
    public string? Role { get; init; }
}

internal sealed record OrganizationCapabilitiesResponse(
    bool CanUpdateOrganization,
    bool CanDeleteOrganization,
    bool CanAddMembers,
    bool CanUpdateMemberRoles,
    bool CanManageTeams,
    bool CanManageInvitations);

internal sealed record OrganizationSummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    string CanonicalKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CurrentRole,
    OrganizationCapabilitiesResponse Capabilities);

internal sealed record OrganizationDetailResponse(
    Guid Id,
    string Name,
    string Slug,
    string CanonicalKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CurrentRole,
    OrganizationCapabilitiesResponse Capabilities,
    IReadOnlyList<string> AllowedEmailDomains);

internal sealed record OrganizationPageResponse(
    IReadOnlyList<OrganizationSummaryResponse> Items,
    string? NextCursor);

internal sealed record OrganizationDeletionResponse(Guid OrganizationId);

internal sealed record ActiveOrganizationResponse(Guid OrganizationId);

internal sealed record OrganizationMemberResponse(
    Guid Id,
    Guid UserId,
    string Name,
    string Email,
    string? ImageUrl,
    string Role,
    DateTimeOffset JoinedAt,
    string? EmailDomain,
    bool IsOutsideAllowedEmailDomains);

internal sealed record OrganizationMemberPageResponse(
    IReadOnlyList<OrganizationMemberResponse> Items,
    string? NextCursor);
