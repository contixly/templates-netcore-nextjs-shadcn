namespace Template.Api.Errors;

internal static class ApiProblemCodes
{
    internal const string InvalidRequest = "invalid_request";
    internal const string ValidationFailed = "validation_failed";
    internal const string Unauthorized = "unauthorized";
    internal const string Forbidden = "forbidden";
    internal const string NotFound = "not_found";
    internal const string MethodNotAllowed = "method_not_allowed";
    internal const string InternalError = "internal_error";
    internal const string AntiforgeryFailed = "antiforgery_failed";
    internal const string LocalAuthInvalidCredentials = "local_auth_invalid_credentials";
    internal const string LocalAuthUserRequired = "local_auth_user_required";
    internal const string LocalAuthDisabled = "local_auth_disabled";
    internal const string LocalAuthUserExists = "local_auth_user_exists";
    internal const string RateLimited = "rate_limited";
    internal const string InvalidReturnUrl = "invalid_return_url";
    internal const string ExternalProviderNotConfigured =
        "external_provider_not_configured";
    internal const string AlreadyAuthenticated = "already_authenticated";
    internal const string ExternalAuthFailed = "external_auth_failed";
    internal const string ExternalEmailRequired = "external_email_required";
    internal const string ExternalEmailUnverified =
        "external_email_unverified";
    internal const string ExternalIdentityConflict =
        "external_identity_conflict";
    internal const string ExternalEmailConflict = "external_email_conflict";
    internal const string OAuthFlowContextChanged =
        "oauth_flow_context_changed";
    internal const string InvalidCursor = "invalid_cursor";
    internal const string ExternalConnectionRequired =
        "external_connection_required";
    internal const string ExternalConnectionNotFound =
        "external_connection_not_found";
    internal const string AccountSessionNotFound =
        "account_session_not_found";
    internal const string CurrentSessionCannotBeRevoked =
        "current_session_cannot_be_revoked";
    internal const string ConcurrencyConflict = "concurrency_conflict";
    internal const string OrganizationOwnershipTransferRequired =
        "organization_ownership_transfer_required";
}
