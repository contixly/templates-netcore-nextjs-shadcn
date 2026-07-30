using Template.Domain.Authentication;

namespace Template.Application.Organizations.Ports;

public interface IOrganizationUserLifecycleStore
{
    Task<OrganizationUserDeletionPreparation> PrepareDeletionAsync(
        UserId userId,
        CancellationToken cancellationToken);
}

public sealed record OrganizationUserDeletionPreparation(
    int DeletedOrganizations,
    bool OwnershipTransferRequired);

public sealed class OrganizationUserLifecycleConcurrencyException : Exception;
