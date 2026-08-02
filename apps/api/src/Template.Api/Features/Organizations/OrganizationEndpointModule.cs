using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Features.Auth;
using Template.Api.Features.ApiKeys;
using Template.Api.OpenApi;
using Template.Application.ApiKeys;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Application.Organizations;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Api.Features.Organizations;

internal sealed class OrganizationEndpointModule : IEndpointModule
{
    private const int DefaultPageLimit = 50;
    private const int MinimumPageLimit = 1;
    private const int MaximumPageLimit = 100;

    public void MapEndpoints(EndpointRouteContext context)
    {
        context.VersionedMixedApi.MapGet(
                "/organizations",
                ListOrganizationsAsync)
            .WithName("GetOrganizations")
            .RequireApiKeyScopes(ApiKeyScopes.OrganizationRead)
            .Produces<ApiResponse<OrganizationPageResponse>>()
            .ProducesValidationProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPost(
                "/organizations",
                CreateOrganizationAsync)
            .WithName("CreateOrganization")
            .AcceptsManuallyReadJson<CreateOrganizationRequest>(
                isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<OrganizationDetailResponse>>(
                StatusCodes.Status201Created)
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedMachineApi.MapGet(
                "/organizations/{organizationId}",
                GetMachineOrganizationAsync)
            .WithName("GetMachineOrganization")
            .RequireApiKeyScopes(ApiKeyScopes.OrganizationRead)
            .Produces<ApiResponse<MachineOrganizationDetailResponse>>()
            .ProducesValidationProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapGet(
                "/organizations/by-key/{organizationKey}",
                GetOrganizationByKeyAsync)
            .WithName("GetOrganizationByKey")
            .Produces<ApiResponse<OrganizationDetailResponse>>()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPatch(
                "/organizations/{organizationId}",
                UpdateOrganizationAsync)
            .WithName("UpdateOrganization")
            .AcceptsManuallyReadJson<UpdateOrganizationRequest>(
                isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<OrganizationDetailResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapDelete(
                "/organizations/{organizationId}",
                DeleteOrganizationAsync)
            .WithName("DeleteOrganization")
            .AcceptsManuallyReadJson<DeleteOrganizationRequest>(
                isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<OrganizationDeletionResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPut(
                "/auth/session/active-organization",
                SetActiveOrganizationAsync)
            .WithName("SetActiveOrganization")
            .AcceptsManuallyReadJson<SetActiveOrganizationRequest>(
                isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<ActiveOrganizationResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedMixedApi.MapGet(
                "/organizations/{organizationId}/members",
                ListOrganizationMembersAsync)
            .WithName("GetOrganizationMembers")
            .RequireApiKeyScopes(
                ApiKeyScopes.OrganizationRead,
                ApiKeyScopes.MemberRead)
            .Produces<ApiResponse<OrganizationMemberPageResponse>>()
            .ProducesValidationProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPost(
                "/organizations/{organizationId}/members",
                AddOrganizationMemberAsync)
            .WithName("AddOrganizationMember")
            .AcceptsManuallyReadJson<AddOrganizationMemberRequest>(
                isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<OrganizationMemberResponse>>(
                StatusCodes.Status201Created)
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPatch(
                "/organizations/{organizationId}/members/{memberId}",
                UpdateOrganizationMemberRoleAsync)
            .WithName("UpdateOrganizationMemberRole")
            .AcceptsManuallyReadJson<UpdateOrganizationMemberRoleRequest>(
                isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<OrganizationMemberResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();
    }

    private static async Task<IResult> ListOrganizationsAsync(
        string? cursor,
        string? limit,
        OrganizationService organizations,
        MachineApiService machineApi,
        IBrowserSessionGateway browserSessions,
        ILogger<OrganizationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        if (ApiKeyPrincipalReader.TryRead(http.User, out var principal))
        {
            return await AuditMachineAsync(
                async () =>
                {
                    var result = await machineApi.ListOrganizationsAsync(
                        principal,
                        cursor,
                        ValidateLimit(limit),
                        cancellationToken);
                    var page = RequireMachineSuccess(result);
                    WriteMachineAudit(
                        logger,
                        "organization_list",
                        "succeeded",
                        principal);
                    return Results.Ok(new ApiResponse<OrganizationPageResponse>(
                        new(
                            page.Items.Select(Map).ToArray(),
                            page.NextCursor)));
                },
                "organization_list",
                principal,
                logger);
        }

        var actor = await RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        var pageLimit = await AuditBoundaryAsync(
            () => Task.FromResult(ValidateLimit(limit)),
            "organization_list",
            actor,
            logger);
        var result = await organizations.ListAsync(
            actor.UserId,
            cursor,
            pageLimit,
            cancellationToken);
        var page = RequireSuccess(
            result,
            "organization_list",
            actor,
            logger,
            organizationId: null,
            memberId: null);
        OrganizationSecurityEvents.Write(
            logger,
            "organization_list",
            "succeeded",
            actor.UserId.Value,
            actor.SessionId.Value,
            organizationId: null,
            memberId: null);
        return Results.Ok(new ApiResponse<OrganizationPageResponse>(
            new(
                page.Items.Select(Map).ToArray(),
                page.NextCursor)));
    }

    private static async Task<IResult> GetMachineOrganizationAsync(
        string organizationId,
        MachineApiService machineApi,
        ILogger<OrganizationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        if (!ApiKeyPrincipalReader.TryRead(http.User, out var principal))
        {
            throw new InvalidOperationException(
                "An authenticated API key principal is required.");
        }

        return await AuditMachineAsync(
            async () =>
            {
                var id = ValidateOrganizationId(organizationId);
                var result = await machineApi.GetOrganizationAsync(
                    principal,
                    id,
                    cancellationToken);
                var organization = RequireMachineSuccess(result);
                WriteMachineAudit(
                    logger,
                    "organization_get",
                    "succeeded",
                    principal);
                return Results.Ok(
                    new ApiResponse<MachineOrganizationDetailResponse>(
                        MapDetail(organization)));
            },
            "organization_get",
            principal,
            logger);
    }

    private static async Task<IResult> CreateOrganizationAsync(
        ApiJsonRequestReader reader,
        OrganizationService organizations,
        IBrowserSessionGateway browserSessions,
        ILogger<OrganizationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var actor = await RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        var name = await AuditBoundaryAsync(
            async () =>
            {
                var request = await reader.ReadAsync<CreateOrganizationRequest>(
                    http,
                    emptyBodyFactory: null,
                    cancellationToken);
                return ValidateName(request.Name);
            },
            "organization_create",
            actor,
            logger);
        var result = await organizations.CreateAsync(
            actor.UserId,
            actor.SessionId,
            name,
            cancellationToken);
        var detail = RequireSuccess(
            result,
            "organization_create",
            actor,
            logger,
            organizationId: null,
            memberId: null,
            notFoundCode: ApiProblemCodes.Unauthorized);
        OrganizationSecurityEvents.Write(
            logger,
            "organization_create",
            "succeeded",
            actor.UserId.Value,
            actor.SessionId.Value,
            detail.Id.Value,
            memberId: null);
        return Results.Created(
            $"/api/v1/organizations/by-key/{detail.Slug.Value}",
            new ApiResponse<OrganizationDetailResponse>(Map(detail)));
    }

    private static async Task<IResult> GetOrganizationByKeyAsync(
        string organizationKey,
        OrganizationService organizations,
        IBrowserSessionGateway browserSessions,
        ILogger<OrganizationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var actor = await RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        var key = await AuditBoundaryAsync(
            () => Task.FromResult(ValidateOrganizationKey(organizationKey)),
            "organization_get",
            actor,
            logger);
        var result = await organizations.GetByKeyAsync(
            actor.UserId,
            key,
            cancellationToken);
        var detail = RequireSuccess(
            result,
            "organization_get",
            actor,
            logger,
            organizationId: null,
            memberId: null);
        OrganizationSecurityEvents.Write(
            logger,
            "organization_get",
            "succeeded",
            actor.UserId.Value,
            actor.SessionId.Value,
            detail.Id.Value,
            memberId: null);
        return Results.Ok(
            new ApiResponse<OrganizationDetailResponse>(Map(detail)));
    }

    private static async Task<IResult> UpdateOrganizationAsync(
        string organizationId,
        ApiJsonRequestReader reader,
        OrganizationService organizations,
        IBrowserSessionGateway browserSessions,
        ILogger<OrganizationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var actor = await RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        var boundary = await AuditBoundaryAsync(
            async () =>
            {
                var id = ValidateOrganizationId(organizationId);
                var request = await reader.ReadAsync<UpdateOrganizationRequest>(
                    http,
                    emptyBodyFactory: null,
                    cancellationToken);
                if (request.Name is null
                    && request.Slug is null
                    && request.AllowedEmailDomains is null)
                {
                    throw Validation(
                        "body",
                        "At least one organization field is required.");
                }

                var name = request.Name is null
                    ? null
                    : ValidateName(request.Name);
                string? slug = null;
                if (request.Slug is not null)
                {
                    if (!OrganizationSlug.TryCreate(
                            request.Slug,
                            out var parsedSlug))
                    {
                        throw Validation(
                            "slug",
                            "A canonical organization slug is required.");
                    }

                    slug = parsedSlug.Value;
                }

                IReadOnlyList<string>? domains = null;
                if (request.AllowedEmailDomains is not null)
                {
                    if (request.AllowedEmailDomains.Count >
                        OrganizationContractLimits.MaximumAllowedEmailDomains)
                    {
                        throw Validation(
                            "allowedEmailDomains",
                            "At most 100 allowed email domains may be supplied.");
                    }

                    if (request.AllowedEmailDomains.Any(value => value is null))
                    {
                        throw Validation(
                            "allowedEmailDomains",
                            "Every allowed email domain must be valid.");
                    }

                    var normalization = OrganizationEmailDomainPolicy.Normalize(
                        request.AllowedEmailDomains.Select(value => value!));
                    if (normalization.InvalidValues.Count > 0)
                    {
                        throw Validation(
                            "allowedEmailDomains",
                            "Every allowed email domain must be valid.");
                    }

                    domains = normalization.Domains;
                }

                return new UpdateBoundary(id, name, slug, domains);
            },
            "organization_update",
            actor,
            logger,
            organizationId);

        var result = await organizations.UpdateAsync(
            actor.UserId,
            boundary.OrganizationId,
            boundary.Name,
            boundary.Slug,
            boundary.AllowedEmailDomains,
            cancellationToken);
        var detail = RequireSuccess(
            result,
            "organization_update",
            actor,
            logger,
            boundary.OrganizationId.Value,
            memberId: null);
        OrganizationSecurityEvents.Write(
            logger,
            "organization_update",
            "succeeded",
            actor.UserId.Value,
            actor.SessionId.Value,
            boundary.OrganizationId.Value,
            memberId: null);
        return Results.Ok(
            new ApiResponse<OrganizationDetailResponse>(Map(detail)));
    }

    private static async Task<IResult> DeleteOrganizationAsync(
        string organizationId,
        ApiJsonRequestReader reader,
        OrganizationService organizations,
        IBrowserSessionGateway browserSessions,
        ILogger<OrganizationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var actor = await RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        var boundary = await AuditBoundaryAsync(
            async () =>
            {
                var id = ValidateOrganizationId(organizationId);
                var request = await reader.ReadAsync<DeleteOrganizationRequest>(
                    http,
                    emptyBodyFactory: null,
                    cancellationToken);
                if (string.IsNullOrEmpty(request.ConfirmationName)
                    || request.ConfirmationName.Length > 50)
                {
                    throw Validation(
                        "confirmationName",
                        "An organization confirmation name is required.");
                }

                return new DeleteBoundary(id, request.ConfirmationName);
            },
            "organization_delete",
            actor,
            logger,
            organizationId);

        var result = await organizations.DeleteAsync(
            actor.UserId,
            boundary.OrganizationId,
            boundary.ConfirmationName,
            cancellationToken);
        var deletion = RequireSuccess(
            result,
            "organization_delete",
            actor,
            logger,
            boundary.OrganizationId.Value,
            memberId: null);
        OrganizationSecurityEvents.Write(
            logger,
            "organization_delete",
            "succeeded",
            actor.UserId.Value,
            actor.SessionId.Value,
            boundary.OrganizationId.Value,
            memberId: null);
        return Results.Ok(new ApiResponse<OrganizationDeletionResponse>(
            new(deletion.OrganizationId.Value)));
    }

    private static async Task<IResult> SetActiveOrganizationAsync(
        ApiJsonRequestReader reader,
        OrganizationService organizations,
        IBrowserSessionGateway browserSessions,
        ILogger<OrganizationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var actor = await RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        var id = await AuditBoundaryAsync(
            async () =>
            {
                var request =
                    await reader.ReadAsync<SetActiveOrganizationRequest>(
                        http,
                        emptyBodyFactory: null,
                        cancellationToken);
                if (request.OrganizationId is null
                    || request.OrganizationId == Guid.Empty)
                {
                    throw Validation(
                        "organizationId",
                        "A valid organization ID is required.");
                }

                return new OrganizationId(request.OrganizationId.Value);
            },
            "active_organization_set",
            actor,
            logger);
        var result = await organizations.SetActiveAsync(
            actor.UserId,
            actor.SessionId,
            id,
            cancellationToken);
        var active = RequireSuccess(
            result,
            "active_organization_set",
            actor,
            logger,
            id.Value,
            memberId: null);
        OrganizationSecurityEvents.Write(
            logger,
            "active_organization_set",
            "succeeded",
            actor.UserId.Value,
            actor.SessionId.Value,
            id.Value,
            memberId: null);
        return Results.Ok(new ApiResponse<ActiveOrganizationResponse>(
            new(active.OrganizationId.Value)));
    }

    private static async Task<IResult> ListOrganizationMembersAsync(
        string organizationId,
        string? cursor,
        string? limit,
        OrganizationMembershipService memberships,
        MachineApiService machineApi,
        IBrowserSessionGateway browserSessions,
        ILogger<OrganizationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        if (ApiKeyPrincipalReader.TryRead(http.User, out var principal))
        {
            return await AuditMachineAsync(
                async () =>
                {
                    var id = ValidateOrganizationId(organizationId);
                    var result = await machineApi.ListOrganizationMembersAsync(
                        principal,
                        id,
                        cursor,
                        ValidateLimit(limit),
                        cancellationToken);
                    var page = RequireMachineSuccess(result);
                    WriteMachineAudit(
                        logger,
                        "organization_members_list",
                        "succeeded",
                        principal);
                    return Results.Ok(
                        new ApiResponse<OrganizationMemberPageResponse>(
                            new(
                                page.Items.Select(Map).ToArray(),
                                page.NextCursor)));
                },
                "organization_members_list",
                principal,
                logger);
        }

        var actor = await RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        var boundary = await AuditBoundaryAsync(
            () => Task.FromResult(
                new MemberListBoundary(
                    ValidateOrganizationId(organizationId),
                    ValidateLimit(limit))),
            "organization_members_list",
            actor,
            logger,
            organizationId);
        var result = await memberships.ListAsync(
            actor.UserId,
            boundary.OrganizationId,
            cursor,
            boundary.Limit,
            cancellationToken);
        var page = RequireSuccess(
            result,
            "organization_members_list",
            actor,
            logger,
            boundary.OrganizationId.Value,
            memberId: null);
        OrganizationSecurityEvents.Write(
            logger,
            "organization_members_list",
            "succeeded",
            actor.UserId.Value,
            actor.SessionId.Value,
            boundary.OrganizationId.Value,
            memberId: null);
        return Results.Ok(new ApiResponse<OrganizationMemberPageResponse>(
            new(
                page.Items.Select(Map).ToArray(),
                page.NextCursor)));
    }

    private static async Task<IResult> AddOrganizationMemberAsync(
        string organizationId,
        ApiJsonRequestReader reader,
        OrganizationMembershipService memberships,
        IBrowserSessionGateway browserSessions,
        ILogger<OrganizationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var actor = await RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        var boundary = await AuditBoundaryAsync(
            async () =>
            {
                var id = ValidateOrganizationId(organizationId);
                var request = await reader.ReadAsync<AddOrganizationMemberRequest>(
                    http,
                    emptyBodyFactory: null,
                    cancellationToken);
                if (request.UserId is null || request.UserId == Guid.Empty)
                {
                    throw Validation(
                        "userId",
                        "A valid target user ID is required.");
                }

                return new AddMemberBoundary(
                    id,
                    new UserId(request.UserId.Value),
                    ValidateRole(request.Role),
                    request.AcknowledgeDomainRestriction ?? false);
            },
            "organization_member_add",
            actor,
            logger,
            organizationId);
        var result = await memberships.AddAsync(
            actor.UserId,
            boundary.OrganizationId,
            boundary.TargetUserId,
            boundary.Role,
            boundary.AcknowledgeDomainRestriction,
            cancellationToken);
        var member = RequireSuccess(
            result,
            "organization_member_add",
            actor,
            logger,
            boundary.OrganizationId.Value,
            memberId: null);
        OrganizationSecurityEvents.Write(
            logger,
            "organization_member_add",
            "succeeded",
            actor.UserId.Value,
            actor.SessionId.Value,
            boundary.OrganizationId.Value,
            member.Id.Value);
        return Results.Created(
            $"/api/v1/organizations/{boundary.OrganizationId.Value:D}/members/{member.Id.Value:D}",
            new ApiResponse<OrganizationMemberResponse>(Map(member)));
    }

    private static async Task<IResult> UpdateOrganizationMemberRoleAsync(
        string organizationId,
        string memberId,
        ApiJsonRequestReader reader,
        OrganizationMembershipService memberships,
        IBrowserSessionGateway browserSessions,
        ILogger<OrganizationEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var actor = await RequiredActorAsync(
            browserSessions,
            http.User,
            cancellationToken);
        var boundary = await AuditBoundaryAsync(
            async () =>
            {
                var id = ValidateOrganizationId(organizationId);
                var requestedMemberId = ValidateMemberId(memberId);
                var request =
                    await reader.ReadAsync<UpdateOrganizationMemberRoleRequest>(
                        http,
                        emptyBodyFactory: null,
                        cancellationToken);
                return new UpdateMemberRoleBoundary(
                    id,
                    requestedMemberId,
                    ValidateRole(request.Role));
            },
            "organization_member_role_update",
            actor,
            logger,
            organizationId,
            memberId);
        var result = await memberships.UpdateRoleAsync(
            actor.UserId,
            boundary.OrganizationId,
            boundary.MemberId,
            boundary.Role,
            cancellationToken);
        var member = RequireSuccess(
            result,
            "organization_member_role_update",
            actor,
            logger,
            boundary.OrganizationId.Value,
            boundary.MemberId.Value);
        OrganizationSecurityEvents.Write(
            logger,
            "organization_member_role_update",
            "succeeded",
            actor.UserId.Value,
            actor.SessionId.Value,
            boundary.OrganizationId.Value,
            boundary.MemberId.Value);
        return Results.Ok(
            new ApiResponse<OrganizationMemberResponse>(Map(member)));
    }

    private static async Task<T> AuditBoundaryAsync<T>(
        Func<Task<T>> execute,
        string operation,
        ActorContext actor,
        ILogger logger,
        string? organizationId = null,
        string? memberId = null)
    {
        try
        {
            return await execute();
        }
        catch (ApiValidationException)
        {
            WriteBoundaryAudit(
                operation,
                ApiProblemCodes.ValidationFailed,
                actor,
                logger,
                organizationId,
                memberId);
            throw;
        }
        catch (ApiProblemException problem)
            when (problem.Code is ApiProblemCodes.InvalidRequest or
                ApiProblemCodes.OrganizationNotFound)
        {
            WriteBoundaryAudit(
                operation,
                problem.Code,
                actor,
                logger,
                organizationId,
                memberId);
            throw;
        }
    }

    private static async Task<IResult> AuditMachineAsync(
        Func<Task<IResult>> execute,
        string operation,
        ApiKeyPrincipal principal,
        ILogger logger)
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

    private static T RequireMachineSuccess<T>(
        MachineApiOperationResult<T> result)
        where T : class
    {
        if (result.Succeeded)
        {
            return result.Value
                ?? throw new InvalidOperationException(
                    "A successful machine API operation returned no value.");
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
                ApiProblemCodes.OrganizationNotFound),
            _ => new InvalidOperationException(
                "A failed machine API operation returned no failure.")
        };
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

    private static void WriteBoundaryAudit(
        string operation,
        string outcome,
        ActorContext actor,
        ILogger logger,
        string? organizationId,
        string? memberId) =>
        OrganizationSecurityEvents.Write(
            logger,
            operation,
            outcome,
            actor.UserId.Value,
            actor.SessionId.Value,
            SafeOpaqueId(organizationId),
            SafeOpaqueId(memberId));

    private static Guid? SafeOpaqueId(string? value) =>
        TryParseCanonicalUuid(value, out var id) && id != Guid.Empty
            ? id
            : null;

    private static T RequireSuccess<T>(
        OrganizationOperationResult<T> result,
        string operation,
        ActorContext actor,
        ILogger logger,
        Guid? organizationId,
        Guid? memberId,
        string? notFoundCode = null)
        where T : class
    {
        if (result.Succeeded)
        {
            return result.Value
                ?? throw new InvalidOperationException(
                    "A successful organization operation returned no value.");
        }

        var failure = result.Failure
            ?? throw new InvalidOperationException(
                "A failed organization operation returned no failure.");
        var problem = MapFailure(
            failure,
            result.Acknowledgement,
            notFoundCode ?? ApiProblemCodes.OrganizationNotFound);
        OrganizationSecurityEvents.Write(
            logger,
            operation,
            problem.Code,
            actor.UserId.Value,
            actor.SessionId.Value,
            organizationId,
            memberId);
        throw problem;
    }

    private static ApiProblemException MapFailure(
        OrganizationFailure failure,
        OrganizationDomainAcknowledgement? acknowledgement,
        string notFoundCode) =>
        failure switch
        {
            OrganizationFailure.InvalidCursor => new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidCursor),
            OrganizationFailure.NotFound => new ApiProblemException(
                notFoundCode == ApiProblemCodes.Unauthorized
                    ? StatusCodes.Status401Unauthorized
                    : StatusCodes.Status404NotFound,
                notFoundCode),
            OrganizationFailure.PermissionDenied => new ApiProblemException(
                StatusCodes.Status403Forbidden,
                ApiProblemCodes.OrganizationPermissionDenied),
            OrganizationFailure.NameConflict => new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.OrganizationNameConflict),
            OrganizationFailure.SlugConflict => new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.OrganizationSlugConflict),
            OrganizationFailure.LastAccessibleOrganization =>
                new ApiProblemException(
                    StatusCodes.Status409Conflict,
                    ApiProblemCodes.LastOrganizationRequired),
            OrganizationFailure.ConfirmationMismatch =>
                new ApiProblemException(
                    StatusCodes.Status400BadRequest,
                    ApiProblemCodes.OrganizationConfirmationMismatch),
            OrganizationFailure.TargetUserNotFound =>
                new ApiProblemException(
                    StatusCodes.Status404NotFound,
                    ApiProblemCodes.TargetUserNotFound),
            OrganizationFailure.MemberNotFound => new ApiProblemException(
                StatusCodes.Status404NotFound,
                ApiProblemCodes.MemberNotFound),
            OrganizationFailure.MemberAlreadyExists =>
                new ApiProblemException(
                    StatusCodes.Status409Conflict,
                    ApiProblemCodes.MemberAlreadyExists),
            OrganizationFailure.MemberRoleUnchanged =>
                new ApiProblemException(
                    StatusCodes.Status409Conflict,
                    ApiProblemCodes.MemberRoleUnchanged),
            OrganizationFailure.RoleAssignmentForbidden =>
                new ApiProblemException(
                    StatusCodes.Status403Forbidden,
                    ApiProblemCodes.RoleAssignmentForbidden),
            OrganizationFailure.DomainAcknowledgementRequired =>
                DomainAcknowledgementProblem(acknowledgement),
            OrganizationFailure.OwnershipTransferRequired =>
                new ApiProblemException(
                    StatusCodes.Status409Conflict,
                    ApiProblemCodes.OrganizationOwnershipTransferRequired),
            OrganizationFailure.ConcurrencyConflict =>
                new ApiProblemException(
                    StatusCodes.Status409Conflict,
                    ApiProblemCodes.ConcurrencyConflict),
            OrganizationFailure.InvalidName
                or OrganizationFailure.InvalidSlug
                or OrganizationFailure.InvalidEmailDomain =>
                throw new InvalidOperationException(
                    "HTTP organization validation and application validation disagreed."),
            _ => throw new InvalidOperationException(
                "Unexpected organization operation failure.")
        };

    private static ApiProblemException DomainAcknowledgementProblem(
        OrganizationDomainAcknowledgement? acknowledgement)
    {
        var value = acknowledgement
            ?? throw new InvalidOperationException(
                "Domain acknowledgement details are missing.");
        return new ApiProblemException(
            StatusCodes.Status409Conflict,
            ApiProblemCodes.MemberDomainAcknowledgementRequired,
            new Dictionary<string, object?>
            {
                ["email"] = value.Email,
                ["emailDomain"] = value.EmailDomain,
                ["allowedEmailDomains"] = value.AllowedEmailDomains.ToArray()
            });
    }

    private static OrganizationSummaryResponse Map(OrganizationSummary value) =>
        new(
            value.Id.Value,
            value.Name,
            value.Slug.Value,
            value.Slug.Value,
            value.CreatedAt,
            value.UpdatedAt,
            "user",
            value.CurrentRole.Value,
            Map(value.Capabilities));

    private static OrganizationDetailResponse Map(OrganizationDetail value) =>
        new(
            value.Id.Value,
            value.Name,
            value.Slug.Value,
            value.Slug.Value,
            value.CreatedAt,
            value.UpdatedAt,
            "user",
            value.CurrentRole.Value,
            Map(value.Capabilities),
            value.AllowedEmailDomains);

    private static OrganizationSummaryResponse Map(
        MachineOrganizationSummary value) =>
        new(
            value.Id.Value,
            value.Name,
            value.Slug,
            value.Slug,
            value.CreatedAt,
            value.UpdatedAt,
            value.AccessPrincipal,
            value.CurrentRole,
            Map(value.Capabilities));

    private static MachineOrganizationDetailResponse MapDetail(
        MachineOrganizationSummary value) =>
        new(
            value.Id.Value,
            value.Name,
            value.Slug,
            value.Slug,
            value.CreatedAt,
            value.UpdatedAt,
            value.AccessPrincipal,
            value.CurrentRole,
            Map(value.Capabilities));

    private static OrganizationCapabilitiesResponse Map(
        OrganizationCapabilities value) =>
        new(
            value.CanUpdateOrganization,
            value.CanDeleteOrganization,
            value.CanAddMembers,
            value.CanUpdateMemberRoles,
            value.CanManageTeams,
            value.CanManageInvitations,
            value.CanManageApiKeys);

    private static OrganizationMemberResponse Map(OrganizationMember value) =>
        new(
            value.Id.Value,
            value.UserId.Value,
            value.Name,
            value.Email,
            ProjectHttpsImage(value.ImageUrl),
            value.Role.Value,
            value.JoinedAt,
            value.EmailDomain,
            value.IsOutsideAllowedEmailDomains);

    private static string ValidateName(string? value)
    {
        if (OrganizationNamePolicy.TryNormalize(value, out var name))
        {
            return name;
        }

        var candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length is < 1 or > OrganizationNamePolicy.MaximumLength)
        {
            throw Validation(
                "name",
                "An organization name of at most 50 characters is required.");
        }

        throw Validation(
            "name",
            "The organization name contains an unsupported character.");
    }

    private static OrganizationRole ValidateRole(string? value)
    {
        if (!OrganizationRole.TryParse(value, out var role))
        {
            throw Validation(
                "role",
                "The role must be one of: member, admin, owner.");
        }

        return role;
    }

    private static OrganizationId ValidateOrganizationId(string value)
    {
        if (!TryParseCanonicalUuid(value, out var id) || id == Guid.Empty)
        {
            throw Validation(
                "organizationId",
                "A valid organization ID is required.");
        }

        return new OrganizationId(id);
    }

    private static string ValidateOrganizationKey(string value)
    {
        if (TryParseCanonicalUuid(value, out _) ||
            (OrganizationSlug.TryCreate(value, out var slug) &&
             string.Equals(value, slug.Value, StringComparison.Ordinal)))
        {
            return value;
        }

        throw new ApiProblemException(
            StatusCodes.Status404NotFound,
            ApiProblemCodes.OrganizationNotFound);
    }

    private static OrganizationMemberId ValidateMemberId(string value)
    {
        if (!TryParseCanonicalUuid(value, out var id) || id == Guid.Empty)
        {
            throw Validation(
                "memberId",
                "A valid organization member ID is required.");
        }

        return new OrganizationMemberId(id);
    }

    private static bool TryParseCanonicalUuid(string? value, out Guid id) =>
        Guid.TryParseExact(value, "D", out id) &&
        string.Equals(
            value,
            id.ToString("D"),
            StringComparison.OrdinalIgnoreCase);

    private static int ValidateLimit(string? value)
    {
        if (value is null)
        {
            return DefaultPageLimit;
        }

        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var limit)
            || limit is < MinimumPageLimit or > MaximumPageLimit)
        {
            throw Validation(
                "limit",
                $"The field limit must be between {MinimumPageLimit} and {MaximumPageLimit}.");
        }

        return limit;
    }

    private static ApiValidationException Validation(
        string field,
        string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static async Task<ActorContext> RequiredActorAsync(
        IBrowserSessionGateway sessions,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var claimedUserId = Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            ? new UserId(userId)
            : throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized);
        var current = await sessions.GetCurrentAsync(cancellationToken)
            ?? throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized);
        if (current.User.Id != claimedUserId)
        {
            throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized);
        }

        return new ActorContext(current.User.Id, current.Session.Id);
    }

    private static string? ProjectHttpsImage(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var image)
        && image.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrEmpty(image.Host)
        && string.IsNullOrEmpty(image.UserInfo)
            ? image.AbsoluteUri
            : null;

    private static void NoStore(HttpContext context) =>
        context.Response.Headers.CacheControl = "no-store";

    private sealed record UpdateBoundary(
        OrganizationId OrganizationId,
        string? Name,
        string? Slug,
        IReadOnlyList<string>? AllowedEmailDomains);

    private sealed record DeleteBoundary(
        OrganizationId OrganizationId,
        string ConfirmationName);

    private sealed record MemberListBoundary(
        OrganizationId OrganizationId,
        int Limit);

    private sealed record AddMemberBoundary(
        OrganizationId OrganizationId,
        UserId TargetUserId,
        OrganizationRole Role,
        bool AcknowledgeDomainRestriction);

    private sealed record UpdateMemberRoleBoundary(
        OrganizationId OrganizationId,
        OrganizationMemberId MemberId,
        OrganizationRole Role);

    private sealed record ActorContext(UserId UserId, SessionId SessionId);
}
