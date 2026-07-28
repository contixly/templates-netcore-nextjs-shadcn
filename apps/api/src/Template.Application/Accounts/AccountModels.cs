using Template.Application.Authentication;
using Template.Domain.Accounts;
using Template.Domain.Authentication;

namespace Template.Application.Accounts;

public enum ExternalAuthIntent
{
    SignIn,
    Connect
}

public sealed record ExternalIdentity(
    ExternalProvider Provider,
    string Subject,
    VerifiedEmail Email,
    string? DisplayName,
    Uri? ImageUrl);

public sealed record ExternalLoginSnapshot(
    UserId UserId,
    ExternalProvider Provider,
    string Subject,
    VerifiedEmail Email,
    DateTimeOffset ConnectedAt,
    DateTimeOffset? LastUsedAt);

public sealed record ExternalAuthentication(
    AuthUser User,
    ExternalProvider Provider,
    bool CreatedUser,
    bool AddedConnection);

public enum AccountFailure
{
    SessionRequired,
    EmailRequired,
    EmailUnverified,
    IdentityConflict,
    EmailConflict,
    ConnectionRequired,
    SessionNotFound,
    CurrentSessionCannotBeRevoked,
    ConfirmationMismatch,
    ConcurrencyConflict
}

public sealed record AccountOperationResult<T>(T? Value, AccountFailure? Failure)
    where T : class;

public sealed class AccountConcurrencyException : Exception;
