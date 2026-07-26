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
}
