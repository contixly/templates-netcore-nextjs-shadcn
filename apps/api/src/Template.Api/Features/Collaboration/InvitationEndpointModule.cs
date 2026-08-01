using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Features.Auth;
using Template.Api.OpenApi;
using Template.Application.Authentication.Ports;
using Template.Application.Collaboration;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Api.Features.Collaboration;

internal sealed record InvitationRateLimitAuditMetadata(
    string Operation,
    string? OrganizationRouteValueName = null,
    string? InvitationRouteValueName = null);

internal sealed class InvitationEndpointModule : IEndpointModule
{
    public void MapEndpoints(EndpointRouteContext context)
    {
        context.VersionedApi.MapGet(
                "/organizations/{organizationId}/invitations",
                ListOrganizationInvitationsAsync)
            .WithName("GetOrganizationInvitations")
            .Produces<ApiResponse<OrganizationInvitationPageResponse>>()
            .ProducesBadRequestVariants()
            .ProducesConflictProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPost(
                "/organizations/{organizationId}/invitations",
                CreateInvitationAsync)
            .WithName("CreateInvitation")
            .AcceptsManuallyReadJson<CreateInvitationRequest>(isOptional: false)
            .RequireApiAntiforgery()
            .RequireRateLimiting(AuthRateLimitPolicies.InvitationCreate)
            .WithMetadata(new InvitationRateLimitAuditMetadata(
                "invitation_create",
                OrganizationRouteValueName: "organizationId"))
            .Produces<ApiResponse<InvitationResponse>>(
                StatusCodes.Status201Created)
            .ProducesBadRequestVariants()
            .ProducesConflictProblem()
            .ProducesRateLimitProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapGet(
                "/account/invitations",
                ListAccountInvitationsAsync)
            .WithName("GetAccountInvitations")
            .Produces<ApiResponse<AccountInvitationPageResponse>>()
            .ProducesBadRequestVariants()
            .ProducesConflictProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapGet(
                "/invitations/{invitationId}",
                GetInvitationDecisionAsync)
            .WithName("GetInvitationDecision")
            .Produces<ApiResponse<InvitationDecisionResponse>>()
            .ProducesBadRequestVariants()
            .ProducesConflictProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPost(
                "/invitations/{invitationId}/accept",
                AcceptInvitationAsync)
            .WithName("AcceptInvitation")
            .RequireApiAntiforgery()
            .RequireRateLimiting(AuthRateLimitPolicies.InvitationDecision)
            .WithMetadata(new InvitationRateLimitAuditMetadata(
                "invitation_accept",
                InvitationRouteValueName: "invitationId"))
            .Produces<ApiResponse<AcceptedInvitationResponse>>()
            .ProducesBadRequestProblem()
            .ProducesValidationProblem()
            .ProducesConflictProblem()
            .ProducesRateLimitProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPost(
                "/invitations/{invitationId}/reject",
                RejectInvitationAsync)
            .WithName("RejectInvitation")
            .RequireApiAntiforgery()
            .RequireRateLimiting(AuthRateLimitPolicies.InvitationDecision)
            .WithMetadata(new InvitationRateLimitAuditMetadata(
                "invitation_reject",
                InvitationRouteValueName: "invitationId"))
            .Produces<ApiResponse<InvitationDecisionResponse>>()
            .ProducesBadRequestProblem()
            .ProducesValidationProblem()
            .ProducesConflictProblem()
            .ProducesRateLimitProblem()
            .ProducesProtectedApiProblems();
    }

    private static async Task<IResult> ListOrganizationInvitationsAsync(
        string organizationId,
        string? status,
        string? cursor,
        string? limit,
        InvitationService invitations,
        IBrowserSessionGateway browserSessions,
        ILogger<InvitationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditInvitationAsync(
            async audit =>
            {
                var organization =
                    CollaborationEndpointBoundary.OrganizationId(
                        organizationId);
                audit.SetOrganizationId(organization);
                var result = await invitations.ListOrganizationAsync(
                    actor.UserId,
                    organization,
                    CollaborationEndpointBoundary.InvitationStatus(http, status),
                    CollaborationEndpointBoundary.Cursor(http, cursor),
                    CollaborationEndpointBoundary.Limit(http, limit),
                    cancellationToken);
                var page = RequireSuccess(result, InvitationOperationKind.Read);
                audit.SetResultCount(page.Items.Count);
                return Results.Ok(
                    new ApiResponse<OrganizationInvitationPageResponse>(
                        Map(page)));
            },
            "invitation_organization_list",
            actor,
            logger,
            organizationId);
    }

    private static async Task<IResult> CreateInvitationAsync(
        string organizationId,
        ApiJsonRequestReader reader,
        InvitationService invitations,
        IBrowserSessionGateway browserSessions,
        ILogger<InvitationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditInvitationAsync(
            async audit =>
            {
                var organization =
                    CollaborationEndpointBoundary.OrganizationId(
                        organizationId);
                audit.SetOrganizationId(organization);
                var request = await reader.ReadAsync<CreateInvitationRequest>(
                    http,
                    emptyBodyFactory: null,
                    cancellationToken);
                var team = CollaborationEndpointBoundary.OptionalTeamId(
                    request.TeamId);
                audit.SetTeamId(team);
                var result = await invitations.CreateAsync(
                    new CreateInvitationCommand(
                        actor.UserId,
                        organization,
                        CollaborationEndpointBoundary.InvitationEmail(
                            request.Email),
                        CollaborationEndpointBoundary.InvitationRole(
                            request.Role),
                        team),
                    cancellationToken);
                var invitation = RequireSuccess(
                    result,
                    InvitationOperationKind.Create);
                audit.SetInvitationId(invitation.Id);
                return Results.Created(
                    $"/api/v1/invitations/{invitation.Id.Value:D}",
                    new ApiResponse<InvitationResponse>(Map(invitation)));
            },
            "invitation_create",
            actor,
            logger,
            organizationId);
    }

    private static async Task<IResult> ListAccountInvitationsAsync(
        string? cursor,
        string? limit,
        InvitationService invitations,
        IBrowserSessionGateway browserSessions,
        ILogger<InvitationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditInvitationAsync(
            async audit =>
            {
                var result = await invitations.ListAccountAsync(
                    actor.InvitationActor,
                    CollaborationEndpointBoundary.Cursor(http, cursor),
                    CollaborationEndpointBoundary.Limit(http, limit),
                    cancellationToken);
                var page = RequireSuccess(result, InvitationOperationKind.Read);
                audit.SetResultCount(page.Items.Count);
                return Results.Ok(
                    new ApiResponse<AccountInvitationPageResponse>(Map(page)));
            },
            "invitation_account_list",
            actor,
            logger);
    }

    private static async Task<IResult> GetInvitationDecisionAsync(
        string invitationId,
        InvitationService invitations,
        IBrowserSessionGateway browserSessions,
        ILogger<InvitationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditInvitationAsync(
            async audit =>
            {
                var id = CollaborationEndpointBoundary.InvitationId(
                    invitationId);
                audit.SetInvitationId(id);
                var result = await invitations.GetDecisionAsync(
                    actor.InvitationActor,
                    id,
                    cancellationToken);
                return Results.Ok(
                    new ApiResponse<InvitationDecisionResponse>(
                        Map(RequireSuccess(
                            result,
                            InvitationOperationKind.Read))));
            },
            "invitation_decision_get",
            actor,
            logger,
            invitationId: invitationId);
    }

    private static async Task<IResult> AcceptInvitationAsync(
        string invitationId,
        InvitationService invitations,
        IBrowserSessionGateway browserSessions,
        ILogger<InvitationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditInvitationAsync(
            async audit =>
            {
                var id = CollaborationEndpointBoundary.InvitationId(
                    invitationId);
                audit.SetInvitationId(id);
                CollaborationEndpointBoundary.RequireEmptyBody(http);
                var result = await invitations.AcceptAsync(
                    new AcceptInvitationCommand(
                        actor.InvitationActor,
                        actor.SessionId,
                        id),
                    cancellationToken);
                return Results.Ok(
                    new ApiResponse<AcceptedInvitationResponse>(
                        Map(RequireSuccess(
                            result,
                            InvitationOperationKind.Mutation))));
            },
            "invitation_accept",
            actor,
            logger,
            invitationId: invitationId);
    }

    private static async Task<IResult> RejectInvitationAsync(
        string invitationId,
        InvitationService invitations,
        IBrowserSessionGateway browserSessions,
        ILogger<InvitationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditInvitationAsync(
            async audit =>
            {
                var id = CollaborationEndpointBoundary.InvitationId(
                    invitationId);
                audit.SetInvitationId(id);
                CollaborationEndpointBoundary.RequireEmptyBody(http);
                var result = await invitations.RejectAsync(
                    new RejectInvitationCommand(actor.InvitationActor, id),
                    cancellationToken);
                return Results.Ok(
                    new ApiResponse<InvitationDecisionResponse>(
                        Map(RequireSuccess(
                            result,
                            InvitationOperationKind.Mutation))));
            },
            "invitation_reject",
            actor,
            logger,
            invitationId: invitationId);
    }

    private static T RequireSuccess<T>(
        InvitationOperationResult<T> result,
        InvitationOperationKind operation)
        where T : class
    {
        if (result.Succeeded)
        {
            return result.Value ?? throw new InvalidOperationException(
                "A successful invitation operation returned no value.");
        }

        throw MapFailure(
            result.Failure ?? throw new InvalidOperationException(
                "A failed invitation operation returned no failure."),
            operation);
    }

    private static ApiProblemException MapFailure(
        InvitationFailure failure,
        InvitationOperationKind operation) =>
        failure switch
        {
            InvitationFailure.InvalidCursor => Problem(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidCursor),
            InvitationFailure.NotFound => Problem(
                StatusCodes.Status404NotFound,
                ApiProblemCodes.InvitationNotFound),
            InvitationFailure.PermissionDenied => Problem(
                StatusCodes.Status403Forbidden,
                ApiProblemCodes.InvitationPermissionDenied),
            InvitationFailure.AlreadyExists => Problem(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.InvitationAlreadyExists),
            InvitationFailure.RecipientAlreadyMember => Problem(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.InvitationRecipientAlreadyMember),
            InvitationFailure.TeamInvalid => Problem(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvitationTeamInvalid),
            InvitationFailure.DomainRestricted => Problem(
                operation == InvitationOperationKind.Create
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status403Forbidden,
                ApiProblemCodes.InvitationDomainRestricted),
            InvitationFailure.RecipientMismatch => Problem(
                StatusCodes.Status403Forbidden,
                ApiProblemCodes.InvitationRecipientMismatch),
            InvitationFailure.EmailVerificationRequired => Problem(
                StatusCodes.Status403Forbidden,
                ApiProblemCodes.InvitationEmailVerificationRequired),
            InvitationFailure.Expired => Problem(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.InvitationExpired),
            InvitationFailure.NotPending => Problem(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.InvitationNotPending),
            InvitationFailure.MembershipConflict => Problem(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.InvitationMembershipConflict),
            InvitationFailure.LimitReached => Problem(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.InvitationLimitReached),
            InvitationFailure.ConcurrencyConflict => Problem(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.ConcurrencyConflict),
            _ => throw new InvalidOperationException(
                "Unexpected invitation operation failure.")
        };

    private static ApiProblemException Problem(int status, string code) =>
        new(status, code);

    private static OrganizationInvitationPageResponse Map(
        OrganizationInvitationPage value) =>
        new(value.Items.Select(Map).ToArray(), value.NextCursor);

    private static AccountInvitationPageResponse Map(
        AccountInvitationPage value) =>
        new(value.Items.Select(Map).ToArray(), value.NextCursor);

    private static InvitationResponse Map(InvitationView value) =>
        new(
            value.Id.Value,
            value.OrganizationId.Value,
            value.OrganizationName,
            value.CanonicalOrganizationKey,
            value.TeamId?.Value,
            value.TeamName,
            value.Email,
            value.Role.Value,
            value.Status.Value,
            value.DisplayState.Value,
            value.ExpiresAt,
            value.CreatedAt,
            value.InviterId.Value,
            value.InviterName,
            $"/invite/{value.Id.Value:D}");

    private static InvitationDecisionResponse Map(InvitationDecision value) =>
        new(
            value.Invitation is null ? null : Map(value.Invitation),
            value.State.Value,
            value.CanRespond);

    private static AcceptedInvitationResponse Map(AcceptedInvitation value) =>
        new(
            value.InvitationId.Value,
            value.OrganizationId.Value,
            value.CanonicalOrganizationKey);

    private enum InvitationOperationKind
    {
        Read,
        Create,
        Mutation
    }
}

internal static class InvitationOpenApiEndpointConventionExtensions
{
    internal static RouteHandlerBuilder ProducesConflictProblem(
        this RouteHandlerBuilder builder) =>
        builder.Produces<ProblemDetails>(
            StatusCodes.Status409Conflict,
            OpenApiDefaults.ProblemContentType);

    internal static RouteHandlerBuilder ProducesRateLimitProblem(
        this RouteHandlerBuilder builder) =>
        builder.Produces<ProblemDetails>(
            StatusCodes.Status429TooManyRequests,
            OpenApiDefaults.ProblemContentType);
}
