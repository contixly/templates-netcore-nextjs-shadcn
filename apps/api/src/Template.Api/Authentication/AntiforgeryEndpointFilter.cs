using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Template.Api.Authentication;

internal sealed class AntiforgeryEndpointFilter(
    IAntiforgery antiforgery,
    IOptions<AntiforgeryOptions> options)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var headerName = options.Value.HeaderName;
        if (string.IsNullOrWhiteSpace(headerName) ||
            string.IsNullOrWhiteSpace(
                context.HttpContext.Request.Headers[headerName].ToString()))
        {
            throw new AntiforgeryValidationException(
                "The required antiforgery request header is missing.");
        }

        await antiforgery.ValidateRequestAsync(context.HttpContext);
        return await next(context);
    }
}
