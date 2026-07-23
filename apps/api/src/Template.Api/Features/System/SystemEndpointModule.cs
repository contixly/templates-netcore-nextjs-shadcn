using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.OpenApi;

namespace Template.Api.Features.System;

internal sealed class SystemEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/system")
            .RequireAuthorization(ApiPolicies.Authenticated);

        group.MapGet(
                "/status",
                ([FromQuery, StringLength(
                    64,
                    MinimumLength = 1,
                    ErrorMessage = "The field echo must be between 1 and 64 characters.")]
                    string? echo,
                    TimeProvider timeProvider) =>
                    TypedResults.Ok(new ApiResponse<SystemStatusResponse>(
                        new SystemStatusResponse(
                            "ok",
                            "1",
                            timeProvider.GetUtcNow(),
                            echo))))
            .AllowAnonymous()
            .WithName("GetSystemStatus")
            .WithSummary("Returns API status and echoes a validated optional value.")
            .Produces<ApiResponse<SystemStatusResponse>>()
            .ProducesValidationProblem()
            .ProducesPublicApiProblems();

        group.MapGet(
                "/authenticated",
                () => TypedResults.Ok(new ApiResponse<AuthenticatedResponse>(
                    new AuthenticatedResponse("authenticated"))))
            .WithName("GetAuthenticatedStatus")
            .WithSummary("Confirms that cookie authentication and authorization succeeded.")
            .Produces<ApiResponse<AuthenticatedResponse>>()
            .ProducesProtectedApiProblems();
    }
}
