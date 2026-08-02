using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.OpenApi;
using Template.Application.Documents;

namespace Template.Api.Features.Documents;

internal sealed class DocumentSearchEndpointModule : IEndpointModule
{
    public void MapEndpoints(EndpointRouteContext context)
    {
        context.VersionedApi.MapGroup("/documents-system")
            .MapGet("/search", Search)
            .WithName("SearchDocumentsSystem")
            .AllowAnonymous()
            .Produces<ApiResponse<DocumentSearchResponse>>()
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(
                StatusCodes.Status406NotAcceptable,
                OpenApiDefaults.ProblemContentType)
            .ProducesPublicApiProblems();
    }

    private static IResult Search(
        string? q,
        string? locale,
        DocumentSearchService search,
        IOptions<DocumentSearchOptions> options,
        HttpContext http)
    {
        DocumentSearchEndpointBoundary.NoStore(http);
        var request = DocumentSearchEndpointBoundary.Request(
            http,
            q,
            locale,
            options);
        var result = search.Search(request);
        return Results.Ok(new ApiResponse<DocumentSearchResponse>(
            DocumentSearchEndpointBoundary.Response(result)));
    }
}
