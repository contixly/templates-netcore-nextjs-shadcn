using Template.Application.Organizations.Ports;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Application.Organizations;

public sealed class OrganizationService(IOrganizationStore organizations)
{
    public async Task<OrganizationOperationResult<OrganizationPage>> ListAsync(
        UserId actorUserId,
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
                return OrganizationOperationResult<OrganizationPage>.Failed(
                    OrganizationFailure.InvalidCursor);
            }

            after = decoded;
        }

        var page = await organizations.ListAsync(
            actorUserId,
            after,
            limit,
            cancellationToken);
        return OrganizationOperationResult<OrganizationPage>.Success(
            new OrganizationPage(
                page.Items,
                page.Next is null ? null : OrganizationCursor.Encode(page.Next)));
    }

    public Task<OrganizationOperationResult<OrganizationDetail>> GetByKeyAsync(
        UserId actorUserId,
        string organizationKey,
        CancellationToken cancellationToken) =>
        organizations.GetByKeyAsync(
            actorUserId,
            organizationKey,
            cancellationToken);

    public Task<OrganizationOperationResult<OrganizationDetail>> CreateAsync(
        UserId actorUserId,
        SessionId sessionId,
        string? name,
        CancellationToken cancellationToken)
    {
        if (!OrganizationNamePolicy.TryNormalize(name, out var normalizedName))
        {
            return Task.FromResult(
                OrganizationOperationResult<OrganizationDetail>.Failed(
                    OrganizationFailure.InvalidName));
        }

        return organizations.CreateAsync(
            new CreateOrganizationCommand(
                actorUserId,
                sessionId,
                normalizedName),
            cancellationToken);
    }

    public Task<OrganizationOperationResult<OrganizationDetail>> UpdateAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        string? name,
        string? slug,
        IReadOnlyList<string>? allowedEmailDomains,
        CancellationToken cancellationToken)
    {
        string? normalizedName = null;
        if (name is not null &&
            !OrganizationNamePolicy.TryNormalize(name, out normalizedName))
        {
            return Task.FromResult(
                OrganizationOperationResult<OrganizationDetail>.Failed(
                    OrganizationFailure.InvalidName));
        }

        OrganizationSlug? normalizedSlug = null;
        if (slug is not null)
        {
            if (!OrganizationSlug.TryCreate(slug, out var parsedSlug))
            {
                return Task.FromResult(
                    OrganizationOperationResult<OrganizationDetail>.Failed(
                        OrganizationFailure.InvalidSlug));
            }

            normalizedSlug = parsedSlug;
        }

        IReadOnlyList<string>? normalizedDomains = null;
        if (allowedEmailDomains is not null)
        {
            var normalization = OrganizationEmailDomainPolicy.Normalize(
                allowedEmailDomains);
            if (normalization.InvalidValues.Count > 0)
            {
                return Task.FromResult(
                    OrganizationOperationResult<OrganizationDetail>.Failed(
                        OrganizationFailure.InvalidEmailDomain));
            }

            normalizedDomains = normalization.Domains;
        }

        return organizations.UpdateAsync(
            new UpdateOrganizationCommand(
                actorUserId,
                organizationId,
                normalizedName,
                normalizedSlug,
                normalizedDomains),
            cancellationToken);
    }

    public Task<OrganizationOperationResult<OrganizationDeletion>> DeleteAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        string confirmationName,
        CancellationToken cancellationToken) =>
        organizations.DeleteAsync(
            new DeleteOrganizationCommand(
                actorUserId,
                organizationId,
                confirmationName),
            cancellationToken);

    public Task<OrganizationOperationResult<ActiveOrganization>> SetActiveAsync(
        UserId actorUserId,
        SessionId sessionId,
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        organizations.SetActiveAsync(
            new SetActiveOrganizationCommand(
                actorUserId,
                sessionId,
                organizationId),
            cancellationToken);

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Organization page limit must be between 1 and 100.");
        }
    }
}
