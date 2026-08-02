using Template.Application.ApiKeys.Ports;
using Template.Application.Organizations;
using Template.Domain.ApiKeys;
using Template.Domain.Organizations;

namespace Template.Application.ApiKeys;

public enum MachineApiResource { Me, Organizations, Members, Teams, TeamMembers }

public enum MachineApiFailure
{
    InvalidCursor,
    OrganizationAccessDenied,
    NotFound
}

public sealed record MachineOrganizationSummary(
    OrganizationId Id,
    string Name,
    string Slug,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string AccessPrincipal,
    string CurrentRole,
    OrganizationCapabilities Capabilities);

public sealed record MachineOrganizationPage(
    IReadOnlyList<MachineOrganizationSummary> Items,
    string? NextCursor);

public sealed record MachineOrganizationMemberPage(
    IReadOnlyList<OrganizationMember> Items,
    string? NextCursor);

public sealed record MachineApiOperationResult<T>(
    T? Value,
    MachineApiFailure? Failure)
    where T : class
{
    public bool Succeeded => Failure is null;

    public static MachineApiOperationResult<T> Success(T value) =>
        new(value, null);

    public static MachineApiOperationResult<T> Failed(
        MachineApiFailure failure) =>
        new(null, failure);
}

public sealed class MachineApiService(IMachineApiStore store)
{
    public static IReadOnlyList<string> RequiredScopes(
        MachineApiResource resource) => resource switch
        {
            MachineApiResource.Me => [ApiKeyScopes.BasicRead],
            MachineApiResource.Organizations => [ApiKeyScopes.OrganizationRead],
            MachineApiResource.Members =>
                [ApiKeyScopes.OrganizationRead, ApiKeyScopes.MemberRead],
            MachineApiResource.Teams =>
                [ApiKeyScopes.OrganizationRead, ApiKeyScopes.TeamRead],
            MachineApiResource.TeamMembers =>
                [
                    ApiKeyScopes.OrganizationRead,
                ApiKeyScopes.TeamRead,
                ApiKeyScopes.TeamMemberRead
                ],
            _ => throw new ArgumentOutOfRangeException(nameof(resource))
        };

    public async Task<MachineApiOperationResult<MachineOrganizationPage>>
        ListOrganizationsAsync(
            ApiKeyPrincipal principal,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        OrganizationListCursorPosition? after = null;
        if (cursor is not null)
        {
            if (!OrganizationCursor.TryDecode(
                    cursor,
                    out OrganizationListCursorPosition decoded))
            {
                return MachineApiOperationResult<
                    MachineOrganizationPage>.Failed(
                        MachineApiFailure.InvalidCursor);
            }

            after = decoded;
        }

        if (principal.Owner.Kind == ApiKeyOwnerKind.User)
        {
            var userId = principal.Owner.UserId
                ?? throw InvalidPrincipal();
            var page = await store.ListUserOrganizationsAsync(
                userId,
                after,
                limit,
                cancellationToken);
            return MachineApiOperationResult<MachineOrganizationPage>.Success(
                new(
                    page.Items,
                    page.Next is null
                        ? null
                        : OrganizationCursor.Encode(page.Next)));
        }

        var organizationId = RequiredOrganizationOwner(principal);
        var organization = await store.GetOrganizationAsync(
            organizationId,
            cancellationToken);
        return MachineApiOperationResult<MachineOrganizationPage>.Success(
            new(
                organization is null ? [] : [organization],
                null));
    }

    public async Task<MachineApiOperationResult<MachineOrganizationSummary>>
        GetOrganizationAsync(
            ApiKeyPrincipal principal,
            OrganizationId organizationId,
            CancellationToken cancellationToken)
    {
        MachineOrganizationSummary? organization;
        if (principal.Owner.Kind == ApiKeyOwnerKind.User)
        {
            var userId = principal.Owner.UserId
                ?? throw InvalidPrincipal();
            organization = await store.GetUserOrganizationAsync(
                userId,
                organizationId,
                cancellationToken);
            return organization is null
                ? MachineApiOperationResult<MachineOrganizationSummary>.Failed(
                    MachineApiFailure.OrganizationAccessDenied)
                : MachineApiOperationResult<MachineOrganizationSummary>.Success(
                    organization);
        }

        var ownerOrganizationId = RequiredOrganizationOwner(principal);
        if (ownerOrganizationId != organizationId)
        {
            return MachineApiOperationResult<MachineOrganizationSummary>.Failed(
                MachineApiFailure.OrganizationAccessDenied);
        }

        organization = await store.GetOrganizationAsync(
            organizationId,
            cancellationToken);
        return organization is null
            ? MachineApiOperationResult<MachineOrganizationSummary>.Failed(
                MachineApiFailure.NotFound)
            : MachineApiOperationResult<MachineOrganizationSummary>.Success(
                organization);
    }

    public async Task<
        MachineApiOperationResult<MachineOrganizationMemberPage>>
        ListOrganizationMembersAsync(
            ApiKeyPrincipal principal,
            OrganizationId organizationId,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        OrganizationMemberCursorPosition? after = null;
        if (cursor is not null)
        {
            if (!OrganizationCursor.TryDecode(
                    cursor,
                    out OrganizationMemberCursorPosition decoded))
            {
                return MachineApiOperationResult<
                    MachineOrganizationMemberPage>.Failed(
                        MachineApiFailure.InvalidCursor);
            }

            after = decoded;
        }

        OrganizationStorePage<
            OrganizationMember,
            OrganizationMemberCursorPosition>? page;
        if (principal.Owner.Kind == ApiKeyOwnerKind.User)
        {
            var userId = principal.Owner.UserId
                ?? throw InvalidPrincipal();
            page = await store.ListUserOrganizationMembersAsync(
                userId,
                organizationId,
                after,
                limit,
                cancellationToken);
            if (page is null)
            {
                return MachineApiOperationResult<
                    MachineOrganizationMemberPage>.Failed(
                        MachineApiFailure.OrganizationAccessDenied);
            }
        }
        else
        {
            var ownerOrganizationId = RequiredOrganizationOwner(principal);
            if (ownerOrganizationId != organizationId)
            {
                return MachineApiOperationResult<
                    MachineOrganizationMemberPage>.Failed(
                        MachineApiFailure.OrganizationAccessDenied);
            }

            page = await store.ListOrganizationMembersAsync(
                organizationId,
                after,
                limit,
                cancellationToken);
            if (page is null)
            {
                return MachineApiOperationResult<
                    MachineOrganizationMemberPage>.Failed(
                        MachineApiFailure.NotFound);
            }
        }

        return MachineApiOperationResult<MachineOrganizationMemberPage>.Success(
            new(
                page.Items,
                page.Next is null
                    ? null
                    : OrganizationCursor.Encode(page.Next)));
    }

    private static OrganizationId RequiredOrganizationOwner(
        ApiKeyPrincipal principal) =>
        principal.Owner.Kind == ApiKeyOwnerKind.Organization &&
        principal.Owner.OrganizationId is not null &&
        principal.Owner.UserId is null
            ? principal.Owner.OrganizationId.Value
            : throw InvalidPrincipal();

    private static InvalidOperationException InvalidPrincipal() => new(
        "The machine principal has an invalid owner projection.");

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Machine API page limit must be between 1 and 100.");
        }
    }
}
