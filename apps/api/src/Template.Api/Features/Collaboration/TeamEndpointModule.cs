using Microsoft.AspNetCore.Mvc;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Features.Auth;
using Template.Api.Features.ApiKeys;
using Template.Api.OpenApi;
using Template.Application.ApiKeys;
using Template.Application.Authentication.Ports;
using Template.Application.Collaboration;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Api.Features.Collaboration;

internal sealed class TeamEndpointModule : IEndpointModule
{
    public void MapEndpoints(EndpointRouteContext context)
    {
        context.VersionedMixedApi.MapGet(
                "/organizations/{organizationId}/teams",
                ListTeamsAsync)
            .WithName("GetTeams")
            .RequireApiKeyScopes(
                ApiKeyScopes.OrganizationRead,
                ApiKeyScopes.TeamRead)
            .Produces<ApiResponse<TeamPageResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPost(
                "/organizations/{organizationId}/teams",
                CreateTeamAsync)
            .WithName("CreateTeam")
            .AcceptsManuallyReadJson<TeamNameRequest>(isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<TeamResponse>>(StatusCodes.Status201Created)
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPatch(
                "/organizations/{organizationId}/teams/{teamId}",
                UpdateTeamAsync)
            .WithName("UpdateTeam")
            .AcceptsManuallyReadJson<TeamNameRequest>(isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<TeamResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapDelete(
                "/organizations/{organizationId}/teams/{teamId}",
                DeleteTeamAsync)
            .WithName("DeleteTeam")
            .RequireApiAntiforgery()
            .Produces<ApiResponse<TeamDeletionResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedMixedApi.MapGet(
                "/organizations/{organizationId}/teams/{teamId}/members",
                ListTeamMembersAsync)
            .WithName("GetTeamMembers")
            .RequireApiKeyScopes(
                ApiKeyScopes.OrganizationRead,
                ApiKeyScopes.TeamRead,
                ApiKeyScopes.TeamMemberRead)
            .Produces<ApiResponse<TeamMemberPageResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPost(
                "/organizations/{organizationId}/teams/{teamId}/members",
                AddTeamMemberAsync)
            .WithName("AddTeamMember")
            .AcceptsManuallyReadJson<AddTeamMemberRequest>(isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<TeamMemberResponse>>(
                StatusCodes.Status201Created)
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapDelete(
                "/organizations/{organizationId}/teams/{teamId}/members/{userId}",
                RemoveTeamMemberAsync)
            .WithName("RemoveTeamMember")
            .RequireApiAntiforgery()
            .Produces<ApiResponse<TeamMemberRemovalResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapGet(
                "/organizations/{organizationId}/teams/{teamId}/member-candidates",
                ListTeamCandidatesAsync)
            .WithName("GetTeamMemberCandidates")
            .Produces<ApiResponse<TeamCandidatePageResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();
    }

    private static async Task<IResult> ListTeamsAsync(
        string organizationId,
        string? cursor,
        string? limit,
        TeamService teams,
        MachineApiService machineApi,
        IBrowserSessionGateway browserSessions,
        ILogger<TeamEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        if (ApiKeyPrincipalReader.TryRead(http.User, out var principal))
        {
            return await AuditMachineAsync(
                async () =>
                {
                    var boundary = new TeamListBoundary(
                        CollaborationEndpointBoundary.OrganizationId(
                            organizationId),
                        CollaborationEndpointBoundary.Cursor(
                            http,
                            cursor),
                        CollaborationEndpointBoundary.Limit(http, limit));
                    var result = await machineApi.ListTeamsAsync(
                        principal,
                        boundary.OrganizationId,
                        boundary.Cursor,
                        boundary.Limit,
                        cancellationToken);
                    var page = RequireMachineSuccess(result);
                    WriteMachineAudit(
                        logger,
                        "team_list",
                        "succeeded",
                        principal);
                    return Results.Ok(
                        new ApiResponse<TeamPageResponse>(Map(page)));
                },
                "team_list",
                principal,
                logger,
                cancellationToken);
        }

        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditAsync(
            async audit =>
            {
                var boundary = new TeamListBoundary(
                    CollaborationEndpointBoundary.OrganizationId(organizationId),
                    CollaborationEndpointBoundary.Cursor(http, cursor),
                    CollaborationEndpointBoundary.Limit(http, limit));
                audit.SetOrganizationId(boundary.OrganizationId);
                var result = await teams.ListAsync(
                    actor.UserId,
                    boundary.OrganizationId,
                    boundary.Cursor,
                    boundary.Limit,
                    cancellationToken);
                var page = RequireSuccess(result);
                audit.SetResultCount(page.Items.Count);
                return Results.Ok(
                    new ApiResponse<TeamPageResponse>(Map(page)));
            },
            "team_list",
            actor,
            logger,
            organizationId);
    }

    private static async Task<IResult> CreateTeamAsync(
        string organizationId,
        ApiJsonRequestReader reader,
        TeamService teams,
        IBrowserSessionGateway browserSessions,
        ILogger<TeamEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditAsync(
            async audit =>
            {
                var id = CollaborationEndpointBoundary.OrganizationId(
                    organizationId);
                audit.SetOrganizationId(id);
                var request = await reader.ReadAsync<TeamNameRequest>(
                    http,
                    emptyBodyFactory: null,
                    cancellationToken);
                var boundary = new TeamNameBoundary(
                    id,
                    CollaborationEndpointBoundary.TeamName(request.Name));
                var result = await teams.CreateAsync(
                    actor.UserId,
                    boundary.OrganizationId,
                    boundary.Name,
                    cancellationToken);
                var team = RequireSuccess(result);
                audit.SetTeamId(team.Id);
                return Results.Created(
                    $"/api/v1/organizations/{boundary.OrganizationId.Value:D}/teams/{team.Id.Value:D}",
                    new ApiResponse<TeamResponse>(Map(team)));
            },
            "team_create",
            actor,
            logger,
            organizationId);
    }

    private static async Task<IResult> UpdateTeamAsync(
        string organizationId,
        string teamId,
        ApiJsonRequestReader reader,
        TeamService teams,
        IBrowserSessionGateway browserSessions,
        ILogger<TeamEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditAsync(
            async audit =>
            {
                var organization =
                    CollaborationEndpointBoundary.OrganizationId(
                        organizationId);
                var team = CollaborationEndpointBoundary.TeamId(teamId);
                audit.SetOrganizationId(organization);
                audit.SetTeamId(team);
                var request = await reader.ReadAsync<TeamNameRequest>(
                    http,
                    emptyBodyFactory: null,
                    cancellationToken);
                var boundary = new TeamResourceNameBoundary(
                    organization,
                    team,
                    CollaborationEndpointBoundary.TeamName(request.Name));
                var result = await teams.UpdateAsync(
                    actor.UserId,
                    boundary.OrganizationId,
                    boundary.TeamId,
                    boundary.Name,
                    cancellationToken);
                var updated = RequireSuccess(result);
                return Results.Ok(
                    new ApiResponse<TeamResponse>(Map(updated)));
            },
            "team_update",
            actor,
            logger,
            organizationId,
            teamId);
    }

    private static async Task<IResult> DeleteTeamAsync(
        string organizationId,
        string teamId,
        TeamService teams,
        IBrowserSessionGateway browserSessions,
        ILogger<TeamEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditAsync(
            async audit =>
            {
                var boundary = new TeamResourceBoundary(
                    CollaborationEndpointBoundary.OrganizationId(organizationId),
                    CollaborationEndpointBoundary.TeamId(teamId));
                audit.SetOrganizationId(boundary.OrganizationId);
                audit.SetTeamId(boundary.TeamId);
                CollaborationEndpointBoundary.RequireEmptyBody(http);
                var result = await teams.DeleteAsync(
                    actor.UserId,
                    boundary.OrganizationId,
                    boundary.TeamId,
                    cancellationToken);
                var deletion = RequireSuccess(result);
                return Results.Ok(new ApiResponse<TeamDeletionResponse>(
                    new(deletion.TeamId.Value)));
            },
            "team_delete",
            actor,
            logger,
            organizationId,
            teamId);
    }

    private static async Task<IResult> ListTeamMembersAsync(
        string organizationId,
        string teamId,
        string? cursor,
        string? limit,
        TeamService teams,
        MachineApiService machineApi,
        IBrowserSessionGateway browserSessions,
        ILogger<TeamEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        if (ApiKeyPrincipalReader.TryRead(http.User, out var principal))
        {
            return await AuditMachineAsync(
                async () =>
                {
                    var boundary = new TeamResourceListBoundary(
                        CollaborationEndpointBoundary.OrganizationId(
                            organizationId),
                        CollaborationEndpointBoundary.TeamId(teamId),
                        CollaborationEndpointBoundary.Cursor(http, cursor),
                        CollaborationEndpointBoundary.Limit(http, limit));
                    var result = await machineApi.ListTeamMembersAsync(
                        principal,
                        boundary.OrganizationId,
                        boundary.TeamId,
                        boundary.Cursor,
                        boundary.Limit,
                        cancellationToken);
                    var page = RequireMachineSuccess(result);
                    WriteMachineAudit(
                        logger,
                        "team_members_list",
                        "succeeded",
                        principal);
                    return Results.Ok(
                        new ApiResponse<TeamMemberPageResponse>(Map(page)));
                },
                "team_members_list",
                principal,
                logger,
                cancellationToken);
        }

        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditAsync(
            async audit =>
            {
                var boundary = new TeamResourceListBoundary(
                    CollaborationEndpointBoundary.OrganizationId(organizationId),
                    CollaborationEndpointBoundary.TeamId(teamId),
                    CollaborationEndpointBoundary.Cursor(http, cursor),
                    CollaborationEndpointBoundary.Limit(http, limit));
                audit.SetOrganizationId(boundary.OrganizationId);
                audit.SetTeamId(boundary.TeamId);
                var result = await teams.ListMembersAsync(
                    actor.UserId,
                    boundary.OrganizationId,
                    boundary.TeamId,
                    boundary.Cursor,
                    boundary.Limit,
                    cancellationToken);
                var page = RequireSuccess(result);
                audit.SetResultCount(page.Items.Count);
                return Results.Ok(
                    new ApiResponse<TeamMemberPageResponse>(Map(page)));
            },
            "team_members_list",
            actor,
            logger,
            organizationId,
            teamId);
    }

    private static async Task<IResult> AddTeamMemberAsync(
        string organizationId,
        string teamId,
        ApiJsonRequestReader reader,
        TeamService teams,
        IBrowserSessionGateway browserSessions,
        ILogger<TeamEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditAsync(
            async audit =>
            {
                var organization =
                    CollaborationEndpointBoundary.OrganizationId(
                        organizationId);
                var team = CollaborationEndpointBoundary.TeamId(teamId);
                audit.SetOrganizationId(organization);
                audit.SetTeamId(team);
                var request = await reader.ReadAsync<AddTeamMemberRequest>(
                    http,
                    emptyBodyFactory: null,
                    cancellationToken);
                var boundary = new TeamMemberBoundary(
                    organization,
                    team,
                    CollaborationEndpointBoundary.UserId(request.UserId));
                audit.SetTargetUserId(boundary.TargetUserId);
                var result = await teams.AddMemberAsync(
                    actor.UserId,
                    boundary.OrganizationId,
                    boundary.TeamId,
                    boundary.TargetUserId,
                    cancellationToken);
                var member = RequireSuccess(result);
                return Results.Created(
                    $"/api/v1/organizations/{boundary.OrganizationId.Value:D}/teams/{boundary.TeamId.Value:D}/members/{member.UserId.Value:D}",
                    new ApiResponse<TeamMemberResponse>(Map(member)));
            },
            "team_member_add",
            actor,
            logger,
            organizationId,
            teamId);
    }

    private static async Task<IResult> RemoveTeamMemberAsync(
        string organizationId,
        string teamId,
        string userId,
        TeamService teams,
        IBrowserSessionGateway browserSessions,
        ILogger<TeamEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditAsync(
            async audit =>
            {
                var boundary = new TeamMemberBoundary(
                    CollaborationEndpointBoundary.OrganizationId(organizationId),
                    CollaborationEndpointBoundary.TeamId(teamId),
                    CollaborationEndpointBoundary.UserId(userId));
                audit.SetOrganizationId(boundary.OrganizationId);
                audit.SetTeamId(boundary.TeamId);
                audit.SetTargetUserId(boundary.TargetUserId);
                CollaborationEndpointBoundary.RequireEmptyBody(http);
                var result = await teams.RemoveMemberAsync(
                    actor.UserId,
                    boundary.OrganizationId,
                    boundary.TeamId,
                    boundary.TargetUserId,
                    cancellationToken);
                var removal = RequireSuccess(result);
                return Results.Ok(new ApiResponse<TeamMemberRemovalResponse>(
                    new(removal.TeamId.Value, removal.UserId.Value)));
            },
            "team_member_remove",
            actor,
            logger,
            organizationId,
            teamId,
            userId);
    }

    private static async Task<IResult> ListTeamCandidatesAsync(
        string organizationId,
        string teamId,
        string? q,
        string? cursor,
        string? limit,
        TeamService teams,
        IBrowserSessionGateway browserSessions,
        ILogger<TeamEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        CollaborationEndpointBoundary.NoStore(http);
        var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        return await CollaborationEndpointBoundary.AuditAsync(
            async audit =>
            {
                var boundary = new TeamCandidateListBoundary(
                    CollaborationEndpointBoundary.OrganizationId(organizationId),
                    CollaborationEndpointBoundary.TeamId(teamId),
                    CollaborationEndpointBoundary.CandidateQuery(http, q),
                    CollaborationEndpointBoundary.Cursor(http, cursor),
                    CollaborationEndpointBoundary.Limit(http, limit));
                audit.SetOrganizationId(boundary.OrganizationId);
                audit.SetTeamId(boundary.TeamId);
                var result = await teams.ListCandidatesAsync(
                    actor.UserId,
                    boundary.OrganizationId,
                    boundary.TeamId,
                    boundary.Query,
                    boundary.Cursor,
                    boundary.Limit,
                    cancellationToken);
                var page = RequireSuccess(result);
                audit.SetResultCount(page.Items.Count);
                return Results.Ok(
                    new ApiResponse<TeamCandidatePageResponse>(Map(page)));
            },
            "team_candidates_list",
            actor,
            logger,
            organizationId,
            teamId);
    }

    private static T RequireSuccess<T>(
        TeamOperationResult<T> result)
        where T : class
    {
        if (result.Succeeded)
        {
            return result.Value
                ?? throw new InvalidOperationException(
                    "A successful team operation returned no value.");
        }

        var failure = result.Failure
            ?? throw new InvalidOperationException(
                "A failed team operation returned no failure.");
        throw MapFailure(failure);
    }

    private static T RequireMachineSuccess<T>(
        MachineApiOperationResult<T> result)
        where T : class
    {
        if (result.Succeeded)
        {
            return result.Value
                ?? throw new InvalidOperationException(
                    "A successful machine team operation returned no value.");
        }

        throw result.Failure switch
        {
            MachineApiFailure.InvalidCursor => new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidCursor),
            MachineApiFailure.OrganizationAccessDenied =>
                new ApiProblemException(
                    StatusCodes.Status403Forbidden,
                    ApiProblemCodes.OrganizationAccessDenied),
            MachineApiFailure.NotFound => new ApiProblemException(
                StatusCodes.Status404NotFound,
                ApiProblemCodes.TeamNotFound),
            _ => new InvalidOperationException(
                "A failed machine team operation returned no failure.")
        };
    }

    private static async Task<IResult> AuditMachineAsync(
        Func<Task<IResult>> execute,
        string operation,
        ApiKeyPrincipal principal,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return await execute();
        }
        catch (ApiValidationException)
        {
            WriteMachineAudit(
                logger,
                operation,
                ApiProblemCodes.ValidationFailed,
                principal);
            throw;
        }
        catch (ApiProblemException problem)
        {
            WriteMachineAudit(logger, operation, problem.Code, principal);
            throw;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            WriteMachineAudit(
                logger,
                operation,
                ApiProblemCodes.InternalError,
                principal);
            throw;
        }
    }

    private static void WriteMachineAudit(
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

    private static ApiProblemException MapFailure(TeamFailure failure) =>
        failure switch
        {
            TeamFailure.InvalidCursor => new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidCursor),
            TeamFailure.NotFound => new ApiProblemException(
                StatusCodes.Status404NotFound,
                ApiProblemCodes.TeamNotFound),
            TeamFailure.PermissionDenied => new ApiProblemException(
                StatusCodes.Status403Forbidden,
                ApiProblemCodes.TeamPermissionDenied),
            TeamFailure.NameConflict => new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.TeamNameConflict),
            TeamFailure.NameUnchanged => new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.TeamNameUnchanged),
            TeamFailure.MemberNotFound => new ApiProblemException(
                StatusCodes.Status404NotFound,
                ApiProblemCodes.TeamMemberNotFound),
            TeamFailure.MemberAlreadyExists => new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.TeamMemberAlreadyExists),
            TeamFailure.ConcurrencyConflict => new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.ConcurrencyConflict),
            TeamFailure.InvalidName => throw new InvalidOperationException(
                "HTTP team-name validation and application validation disagreed."),
            _ => throw new InvalidOperationException(
                "Unexpected team operation failure.")
        };

    private static TeamPageResponse Map(TeamPage value) => new(
        value.Items.Select(Map).ToArray(),
        value.NextCursor);

    private static TeamPageResponse Map(MachineTeamPage value) => new(
        value.Items.Select(Map).ToArray(),
        value.NextCursor);

    private static TeamResponse Map(TeamSummary value) => new(
        value.Id.Value,
        value.OrganizationId.Value,
        value.Name.Value,
        value.MemberCount,
        Map(value.Members),
        value.CreatedAt,
        value.UpdatedAt,
        MembersIncluded: true);

    private static TeamResponse Map(MachineTeamSummary value) => new(
        value.Id.Value,
        value.OrganizationId.Value,
        value.Name.Value,
        value.MemberCount,
        Map(value.Members),
        value.CreatedAt,
        value.UpdatedAt,
        value.MembersIncluded);

    private static TeamMemberPageResponse Map(TeamMemberPage value) => new(
        value.Items.Select(Map).ToArray(),
        value.NextCursor);

    private static TeamMemberResponse Map(TeamMemberView value) => new(
        value.Id.Value,
        value.UserId.Value,
        value.Name,
        value.Email,
        ProjectHttpsImage(value.ImageUrl),
        value.Role.Value,
        value.OrganizationJoinedAt,
        value.TeamJoinedAt);

    private static TeamCandidatePageResponse Map(TeamCandidatePage value) => new(
        value.Items.Select(Map).ToArray(),
        value.NextCursor);

    private static TeamCandidateResponse Map(TeamCandidate value) => new(
        value.MemberId.Value,
        value.UserId.Value,
        value.Name,
        value.Email,
        ProjectHttpsImage(value.ImageUrl),
        value.Role.Value,
        value.JoinedAt);

    private static string? ProjectHttpsImage(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var image)
        && image.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrEmpty(image.Host)
        && string.IsNullOrEmpty(image.UserInfo)
            ? image.AbsoluteUri
            : null;

    private sealed record TeamListBoundary(
        OrganizationId OrganizationId,
        string? Cursor,
        int Limit);

    private sealed record TeamNameBoundary(
        OrganizationId OrganizationId,
        string Name);

    private sealed record TeamResourceBoundary(
        OrganizationId OrganizationId,
        TeamId TeamId);

    private sealed record TeamResourceNameBoundary(
        OrganizationId OrganizationId,
        TeamId TeamId,
        string Name);

    private sealed record TeamResourceListBoundary(
        OrganizationId OrganizationId,
        TeamId TeamId,
        string? Cursor,
        int Limit);

    private sealed record TeamMemberBoundary(
        OrganizationId OrganizationId,
        TeamId TeamId,
        UserId TargetUserId);

    private sealed record TeamCandidateListBoundary(
        OrganizationId OrganizationId,
        TeamId TeamId,
        string? Query,
        string? Cursor,
        int Limit);
}
