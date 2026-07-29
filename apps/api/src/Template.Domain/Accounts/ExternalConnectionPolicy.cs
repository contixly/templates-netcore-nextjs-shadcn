using Template.Domain.Authentication;

namespace Template.Domain.Accounts;

public enum EmailOwnershipDecision
{
    ReuseCurrent,
    AttachSecondary,
    ConflictWithOtherUser
}

public static class ExternalConnectionPolicy
{
    public static EmailOwnershipDecision DecideEmailOwnership(
        UserId? currentUser,
        UserId? emailOwner) =>
        emailOwner switch
        {
            null => EmailOwnershipDecision.AttachSecondary,
            _ when emailOwner == currentUser => EmailOwnershipDecision.ReuseCurrent,
            _ => EmailOwnershipDecision.ConflictWithOtherUser
        };

    public static bool CanDisconnect(
        ExternalProvider? currentAuthenticationProvider,
        ExternalProvider candidate,
        int configuredSurvivorCount) =>
        currentAuthenticationProvider != candidate && configuredSurvivorCount > 0;
}
