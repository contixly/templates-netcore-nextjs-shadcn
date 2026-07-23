using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Template.Api.Endpoints;

namespace Template.Api.Tests.Infrastructure;

internal sealed class TestEndpointModule : IEndpointModule
{
    internal const string ForbiddenPolicy = "Test.Forbidden";

    public void MapEndpoints(EndpointRouteContext context)
    {
        context.Root.MapGet("/api/testing/forbidden", () => Results.Ok())
            .RequireAuthorization(ForbiddenPolicy)
            .ExcludeFromDescription();

        context.Root.MapGet("/api/testing/fault", ThrowFault)
            .ExcludeFromDescription();

        context.Root.MapGet("/api/testing/bad-request", ThrowBadRequest)
            .ExcludeFromDescription();

        context.Root.MapGet(
                "/api/testing/nested-validation",
                () => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Address.PostalCode"] = ["Postal code is required."],
                    ["address.PostalCode"] = ["Postal code has an invalid format."],
                    ["ContactInfo.EmailAddress"] = ["Email address is invalid."]
                }))
            .ExcludeFromDescription();

        context.VersionedApi.MapGet("/testing/consumer", () => Results.Ok())
            .ExcludeFromDescription();
    }

    private static IResult ThrowFault() =>
        throw new InvalidOperationException("sensitive-database-message");

    private static IResult ThrowBadRequest() =>
        throw new BadHttpRequestException("test malformed request", StatusCodes.Status400BadRequest);
}
