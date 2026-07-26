using Template.Api.Errors;

namespace Template.Api.Authentication;

internal sealed class LocalAutomationAvailabilityMiddleware(
    RequestDelegate next)
{
    public Task InvokeAsync(
        HttpContext context,
        ILocalAutomationAuthAvailability availability)
    {
        if (context.Request.Path.StartsWithSegments("/api/local-auth") &&
            !availability.IsEnabled)
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                ApiProblemCodes.LocalAuthDisabled);
        }

        return next(context);
    }
}
