using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Application.Authentication;

public sealed record AuthUser(
    UserId Id,
    string Name,
    string Email,
    bool EmailVerified,
    string? Image,
    bool IsLocalAutomation);

public sealed record BrowserSession(
    SessionId Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    string AuthenticationMethod,
    OrganizationId? ActiveOrganizationId = null);

public static class BrowserAuthenticationMethods
{
    public const string Local = "local";
    public const string Google = "google";
    public const string GitHub = "github";
    public const string GitLab = "gitlab";
    public const string Vk = "vk";
    public const string Yandex = "yandex";

    public static bool IsAllowed(string? value) =>
        value is Local or Google or GitHub or GitLab or Vk or Yandex;

    public static string Project(string? value) =>
        IsAllowed(value) ? value! : Local;
}

public sealed record AuthenticatedSession(AuthUser User, BrowserSession Session);

public sealed record SessionState(
    bool Authenticated,
    AuthUser? User,
    BrowserSession? Session)
{
    public static SessionState Anonymous { get; } = new(false, null, null);

    public static SessionState From(AuthenticatedSession value) =>
        new(true, value.User, value.Session);
}

public sealed record LocalAutomationCredentials(
    string Name,
    string Email,
    string Password);

public sealed record CreateLocalScenarioInput(
    string? Name,
    string? Email,
    string? Password);

public sealed record LocalCredentialInput(string Email, string Password);

public sealed record LocalAutomationScenario(
    AuthUser User,
    BrowserSession Session,
    LocalAutomationCredentials Credentials,
    string CleanupUrl);

public sealed record LocalAutomationCleanup(int DeletedOrganizations);

public enum AuthFailure
{
    InvalidLocalEmail,
    UserExists,
    InvalidCredentials,
    SessionRequired,
    LocalUserRequired,
    OrganizationOwnershipTransferRequired
}

public sealed record AuthOperationResult<T>(T? Value, AuthFailure? Failure)
    where T : class
{
    public bool Succeeded => Failure is null;

    public static AuthOperationResult<T> Success(T value) => new(value, null);

    public static AuthOperationResult<T> Failed(AuthFailure failure) =>
        new(null, failure);
}

public sealed class DuplicateLocalIdentityException : Exception;

public sealed class LocalIdentityValidationException : Exception;
