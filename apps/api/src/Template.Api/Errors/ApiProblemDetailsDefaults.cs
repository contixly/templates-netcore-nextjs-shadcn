using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Observability;

namespace Template.Api.Errors;

internal static class ApiProblemDetailsDefaults
{
    internal static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        var isValidation = problem is HttpValidationProblemDetails;
        var requestedCode = problem.Extensions.TryGetValue("code", out var rawCode)
            ? rawCode as string
            : null;
        var definition = Resolve(status, isValidation, requestedCode);

        problem.Status = status;
        problem.Type = $"urn:template:problem:{definition.Code}";
        problem.Title = definition.Title;
        problem.Detail = definition.Detail;
        problem.Instance = context.HttpContext.Request.Path.Value ?? "/";
        problem.Extensions["code"] = definition.Code;
        problem.Extensions["traceId"] =
            CorrelationIdMiddleware.GetTraceId(context.HttpContext);

        if (problem is HttpValidationProblemDetails validation)
        {
            var normalized = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (key, messages) in validation.Errors)
            {
                var normalizedKey = string.Join(
                    '.',
                    key.Split('.')
                        .Select(JsonNamingPolicy.CamelCase.ConvertName));
                if (!normalized.TryGetValue(normalizedKey, out var mergedMessages))
                {
                    mergedMessages = [];
                    normalized[normalizedKey] = mergedMessages;
                }

                mergedMessages.AddRange(messages);
            }

            validation.Errors.Clear();
            foreach (var (key, messages) in normalized)
            {
                validation.Errors[key] = [.. messages];
            }
        }
    }

    private static ProblemDefinition Resolve(
        int status,
        bool isValidation,
        string? requestedCode)
    {
        var custom = requestedCode switch
        {
            ApiProblemCodes.AntiforgeryFailed => new ProblemDefinition(
                requestedCode,
                "Antiforgery validation failed",
                "The request antiforgery token is missing or invalid."),
            ApiProblemCodes.LocalAuthInvalidCredentials => new ProblemDefinition(
                requestedCode,
                "Authentication failed",
                "The supplied local credentials are invalid."),
            ApiProblemCodes.LocalAuthUserRequired => new ProblemDefinition(
                requestedCode,
                "Local automation user required",
                "This operation requires a local automation user."),
            ApiProblemCodes.LocalAuthDisabled => new ProblemDefinition(
                requestedCode,
                "Local authentication unavailable",
                "Local automation authentication is not available."),
            ApiProblemCodes.LocalAuthUserExists => new ProblemDefinition(
                requestedCode,
                "Local automation user already exists",
                "The requested local automation identity cannot be created."),
            ApiProblemCodes.RateLimited => new ProblemDefinition(
                requestedCode,
                "Too many requests",
                "The authentication request rate limit was exceeded."),
            ApiProblemCodes.InvalidReturnUrl => new ProblemDefinition(
                requestedCode,
                "Invalid return path",
                "The return path is not allowed."),
            ApiProblemCodes.ExternalProviderNotConfigured =>
                new ProblemDefinition(
                    requestedCode,
                    "External provider unavailable",
                    "The requested external authentication provider is not configured."),
            ApiProblemCodes.AlreadyAuthenticated => new ProblemDefinition(
                requestedCode,
                "Already authenticated",
                "External sign-in must be started by an anonymous browser."),
            ApiProblemCodes.ExternalAuthFailed => new ProblemDefinition(
                requestedCode,
                "External authentication failed",
                "External authentication could not be completed."),
            ApiProblemCodes.ExternalEmailRequired => new ProblemDefinition(
                requestedCode,
                "External email required",
                "The external provider did not return a usable email."),
            ApiProblemCodes.ExternalEmailUnverified => new ProblemDefinition(
                requestedCode,
                "Verified external email required",
                "The external provider email did not satisfy verification policy."),
            ApiProblemCodes.ExternalIdentityConflict => new ProblemDefinition(
                requestedCode,
                "External identity conflict",
                "The external identity cannot be safely assigned."),
            ApiProblemCodes.ExternalEmailConflict => new ProblemDefinition(
                requestedCode,
                "External email conflict",
                "The verified external email belongs to another account."),
            ApiProblemCodes.OAuthFlowContextChanged => new ProblemDefinition(
                requestedCode,
                "OAuth flow context changed",
                "The browser session no longer matches the OAuth flow."),
            ApiProblemCodes.InvalidCursor => new ProblemDefinition(
                requestedCode,
                "Invalid cursor",
                "The supplied pagination cursor is invalid."),
            ApiProblemCodes.ExternalConnectionRequired =>
                new ProblemDefinition(
                    requestedCode,
                    "External connection required",
                    "The external connection is required to keep the account accessible."),
            ApiProblemCodes.ExternalConnectionNotFound =>
                new ProblemDefinition(
                    requestedCode,
                    "External connection not found",
                    "The requested external connection was not found."),
            ApiProblemCodes.AccountSessionNotFound => new ProblemDefinition(
                requestedCode,
                "Account session not found",
                "The requested account session was not found."),
            ApiProblemCodes.CurrentSessionCannotBeRevoked =>
                new ProblemDefinition(
                    requestedCode,
                    "Current session cannot be revoked",
                    "The current browser session cannot be revoked by this operation."),
            ApiProblemCodes.ConcurrencyConflict => new ProblemDefinition(
                requestedCode,
                "Concurrent update conflict",
                "The resource changed concurrently; retry the operation."),
            ApiProblemCodes.OrganizationNotFound => new ProblemDefinition(
                requestedCode,
                "Organization not found",
                "The requested organization was not found."),
            ApiProblemCodes.OrganizationPermissionDenied =>
                new ProblemDefinition(
                    requestedCode,
                    "Organization permission denied",
                    "You do not have permission to perform this organization operation."),
            ApiProblemCodes.OrganizationNameConflict =>
                new ProblemDefinition(
                    requestedCode,
                    "Organization name conflict",
                    "An accessible organization already uses this name."),
            ApiProblemCodes.OrganizationSlugConflict =>
                new ProblemDefinition(
                    requestedCode,
                    "Organization slug conflict",
                    "An organization already uses this slug."),
            ApiProblemCodes.LastOrganizationRequired =>
                new ProblemDefinition(
                    requestedCode,
                    "Another organization is required",
                    "The last accessible organization cannot be deleted."),
            ApiProblemCodes.OrganizationConfirmationMismatch =>
                new ProblemDefinition(
                    requestedCode,
                    "Organization confirmation mismatch",
                    "The organization confirmation name does not match."),
            ApiProblemCodes.MemberNotFound => new ProblemDefinition(
                requestedCode,
                "Member not found",
                "The requested organization member was not found."),
            ApiProblemCodes.TargetUserNotFound => new ProblemDefinition(
                requestedCode,
                "Target user not found",
                "The requested target user was not found."),
            ApiProblemCodes.MemberAlreadyExists => new ProblemDefinition(
                requestedCode,
                "Member already exists",
                "The target user is already an organization member."),
            ApiProblemCodes.MemberRoleUnchanged => new ProblemDefinition(
                requestedCode,
                "Member role unchanged",
                "The requested organization member already has this role."),
            ApiProblemCodes.RoleAssignmentForbidden =>
                new ProblemDefinition(
                    requestedCode,
                    "Role assignment forbidden",
                    "The requested organization role change is not permitted."),
            ApiProblemCodes.MemberDomainAcknowledgementRequired =>
                new ProblemDefinition(
                    requestedCode,
                    "Email domain acknowledgement required",
                    "The target user's email domain is outside the organization's allowed domains."),
            ApiProblemCodes.OrganizationOwnershipTransferRequired =>
                new ProblemDefinition(
                    requestedCode,
                    "Organization ownership transfer required",
                    "Organization ownership must be transferred before this operation."),
            ApiProblemCodes.TeamNotFound => new ProblemDefinition(
                requestedCode,
                "Team not found",
                "The requested team was not found."),
            ApiProblemCodes.TeamPermissionDenied => new ProblemDefinition(
                requestedCode,
                "Team permission denied",
                "You do not have permission to perform this team operation."),
            ApiProblemCodes.TeamNameConflict => new ProblemDefinition(
                requestedCode,
                "Team name conflict",
                "A team in this organization already uses this name."),
            ApiProblemCodes.TeamNameUnchanged => new ProblemDefinition(
                requestedCode,
                "Team name unchanged",
                "The requested team already uses this name."),
            ApiProblemCodes.TeamMemberNotFound => new ProblemDefinition(
                requestedCode,
                "Team member not found",
                "The requested team member was not found."),
            ApiProblemCodes.TeamMemberAlreadyExists => new ProblemDefinition(
                requestedCode,
                "Team member already exists",
                "The requested user is already a member of this team."),
            _ => null
        };
        if (custom is not null)
        {
            return custom;
        }

        return (status, isValidation) switch
        {
            (StatusCodes.Status400BadRequest, true) => new(
                ApiProblemCodes.ValidationFailed,
                "Request validation failed",
                "One or more validation errors occurred."),
            (StatusCodes.Status400BadRequest, false) => new(
                ApiProblemCodes.InvalidRequest,
                "Invalid request",
                "The request could not be processed."),
            (StatusCodes.Status401Unauthorized, _) => new(
                ApiProblemCodes.Unauthorized,
                "Authentication required",
                "Authentication is required to access this resource."),
            (StatusCodes.Status403Forbidden, _) => new(
                ApiProblemCodes.Forbidden,
                "Access forbidden",
                "You do not have permission to access this resource."),
            (StatusCodes.Status404NotFound, _) => new(
                ApiProblemCodes.NotFound,
                "Resource not found",
                "The requested resource was not found."),
            (StatusCodes.Status405MethodNotAllowed, _) => new(
                ApiProblemCodes.MethodNotAllowed,
                "Method not allowed",
                "The HTTP method is not supported for this resource."),
            _ when status >= StatusCodes.Status500InternalServerError => new(
                ApiProblemCodes.InternalError,
                "Internal server error",
                "An unexpected error occurred."),
            _ => new(
                ApiProblemCodes.InvalidRequest,
                "Invalid request",
                "The request could not be processed.")
        };
    }

    private sealed record ProblemDefinition(string Code, string Title, string Detail);
}
