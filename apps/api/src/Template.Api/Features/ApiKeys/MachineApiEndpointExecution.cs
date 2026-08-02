using Template.Api.Errors;
using Template.Application.ApiKeys;
using Template.Domain.ApiKeys;

namespace Template.Api.Features.ApiKeys;

internal static class MachineApiEndpointExecution
{
    internal static async Task<IResult> ExecuteAsync(
        Func<Task<IResult>> execute,
        string operation,
        ApiKeyPrincipal principal,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await execute();
            WriteAudit(logger, operation, "succeeded", principal);
            return result;
        }
        catch (ApiValidationException)
        {
            WriteAudit(
                logger,
                operation,
                ApiProblemCodes.ValidationFailed,
                principal);
            throw;
        }
        catch (ApiProblemException problem)
        {
            WriteAudit(logger, operation, problem.Code, principal);
            throw;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            WriteAudit(
                logger,
                operation,
                ApiProblemCodes.InternalError,
                principal);
            throw;
        }
    }

    private static void WriteAudit(
        ILogger logger,
        string operation,
        string outcome,
        ApiKeyPrincipal principal) =>
        ApiKeySecurityEvents.WriteMachine(
            logger,
            operation,
            outcome,
            principal.Owner.Kind == ApiKeyOwnerKind.User
                ? "user"
                : "organization",
            principal.Owner.UserId?.Value ??
            principal.Owner.OrganizationId?.Value,
            principal.Id.Value);
}
