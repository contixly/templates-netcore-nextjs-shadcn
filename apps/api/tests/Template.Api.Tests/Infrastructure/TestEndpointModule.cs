using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Template.Api.Endpoints;

namespace Template.Api.Tests.Infrastructure;

internal sealed class TestEndpointModule : IEndpointModule
{
    internal const string ForbiddenPolicy = "Test.Forbidden";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/testing/forbidden", () => Results.Ok())
            .RequireAuthorization(ForbiddenPolicy)
            .ExcludeFromDescription();
    }
}
