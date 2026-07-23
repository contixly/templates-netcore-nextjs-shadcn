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
}
