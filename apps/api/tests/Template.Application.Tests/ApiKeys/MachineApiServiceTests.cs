using Template.Application.ApiKeys;
using Template.Application.ApiKeys.Ports;
using Template.Application.Organizations;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Application.Tests.ApiKeys;

public sealed class MachineApiServiceTests
{
    [Theory]
    [InlineData(MachineApiResource.Me, new[] { "basic:read" })]
    [InlineData(MachineApiResource.Organizations, new[] { "organization:read" })]
    [InlineData(MachineApiResource.Members, new[] { "organization:read", "member:read" })]
    [InlineData(MachineApiResource.Teams, new[] { "organization:read", "team:read" })]
    [InlineData(MachineApiResource.TeamMembers, new[] { "organization:read", "team:read", "teamMember:read" })]
    public void Machine_resources_have_the_exact_required_scope_sets(MachineApiResource resource, string[] expected) =>
        Assert.Equal(expected, MachineApiService.RequiredScopes(resource));

    [Fact]
    public async Task Personal_organization_list_decodes_target_cursor_and_uses_current_user_memberships()
    {
        const string cursor =
            "AQMI3u-zpjcQAAGYp6zQ-HgytxEhH1bFdwOupQyx";
        var userId = new UserId(Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57701"));
        var organizationId = new OrganizationId(Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57702"));
        var after = new OrganizationListCursorPosition(
            DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            new OrganizationMemberId(Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57703")));
        var store = new FakeMachineApiStore
        {
            UserOrganizations = new(
                [UserSummary(organizationId)],
                after)
        };
        var service = new MachineApiService(store);

        var result = await service.ListOrganizationsAsync(
            PersonalPrincipal(userId),
            cursor,
            25,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(userId, store.ListUserId);
        Assert.Equal(after, store.ListAfter);
        Assert.Equal(25, store.ListLimit);
        Assert.Equal("user", Assert.Single(result.Value!.Items).AccessPrincipal);
        Assert.Equal(cursor, result.Value.NextCursor);
    }

    [Fact]
    public async Task Organization_principal_list_never_invents_a_user_and_has_no_continuation()
    {
        var organizationId = new OrganizationId(Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57702"));
        var store = new FakeMachineApiStore
        {
            Organization = OrganizationSummary(organizationId)
        };
        var service = new MachineApiService(store);

        var result = await service.ListOrganizationsAsync(
            OrganizationPrincipal(organizationId),
            cursor: null,
            limit: 50,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        var item = Assert.Single(result.Value!.Items);
        Assert.Null(store.ListUserId);
        Assert.Equal(organizationId, store.GetOrganizationId);
        Assert.Equal("organization", item.AccessPrincipal);
        Assert.Equal("organization", item.CurrentRole);
        Assert.False(item.Capabilities.CanUpdateOrganization);
        Assert.Null(result.Value.NextCursor);
    }

    [Fact]
    public async Task Invalid_machine_organization_cursor_fails_before_persistence()
    {
        var store = new FakeMachineApiStore();
        var service = new MachineApiService(store);

        var result = await service.ListOrganizationsAsync(
            PersonalPrincipal(new UserId(Guid.NewGuid())),
            "not-a-target-cursor",
            50,
            TestContext.Current.CancellationToken);

        Assert.Equal(MachineApiFailure.InvalidCursor, result.Failure);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Personal_and_organization_principals_enforce_current_route_access_before_reading_members()
    {
        var userId = new UserId(Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57701"));
        var ownerId = new OrganizationId(Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57702"));
        var foreignId = new OrganizationId(Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57703"));
        var store = new FakeMachineApiStore();
        var service = new MachineApiService(store);

        var personal = await service.GetOrganizationAsync(
            PersonalPrincipal(userId),
            foreignId,
            TestContext.Current.CancellationToken);
        var organization = await service.ListOrganizationMembersAsync(
            OrganizationPrincipal(ownerId),
            foreignId,
            cursor: null,
            limit: 50,
            TestContext.Current.CancellationToken);

        Assert.Equal(MachineApiFailure.OrganizationAccessDenied, personal.Failure);
        Assert.Equal(MachineApiFailure.OrganizationAccessDenied, organization.Failure);
        Assert.Equal(1, store.Calls);
        Assert.Equal(userId, store.GetUserId);
        Assert.Equal(foreignId, store.GetUserOrganizationId);
    }

    private static ApiKeyPrincipal PersonalPrincipal(UserId userId) => new(
        new ApiKeyId(Guid.NewGuid()),
        "user_abcdefghijk",
        new(ApiKeyOwnerKind.User, userId, null),
        [ApiKeyScopes.OrganizationRead, ApiKeyScopes.MemberRead]);

    private static ApiKeyPrincipal OrganizationPrincipal(
        OrganizationId organizationId) => new(
        new ApiKeyId(Guid.NewGuid()),
        "org_abcdefghijkl",
        new(ApiKeyOwnerKind.Organization, null, organizationId),
        [ApiKeyScopes.OrganizationRead, ApiKeyScopes.MemberRead]);

    private static MachineOrganizationSummary UserSummary(
        OrganizationId organizationId) => new(
        organizationId,
        "User organization",
        "user-organization",
        DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
        DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
        "user",
        "admin",
        OrganizationPermissionPolicy.GetCapabilities(OrganizationRole.Admin));

    private static MachineOrganizationSummary OrganizationSummary(
        OrganizationId organizationId) => new(
        organizationId,
        "Organization principal",
        "organization-principal",
        DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
        DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
        "organization",
        "organization",
        new(false, false, false, false, false, false, false));

    private sealed class FakeMachineApiStore : IMachineApiStore
    {
        public OrganizationStorePage<MachineOrganizationSummary, OrganizationListCursorPosition>
            UserOrganizations
        { get; init; } = new([], null);

        public MachineOrganizationSummary? Organization { get; init; }
        public MachineOrganizationSummary? UserOrganization { get; init; }
        public OrganizationStorePage<OrganizationMember, OrganizationMemberCursorPosition>?
            UserMembers
        { get; init; }
        public OrganizationStorePage<OrganizationMember, OrganizationMemberCursorPosition>?
            OrganizationMembers
        { get; init; }
        public int Calls { get; private set; }
        public UserId? ListUserId { get; private set; }
        public OrganizationListCursorPosition? ListAfter { get; private set; }
        public int? ListLimit { get; private set; }
        public OrganizationId? GetOrganizationId { get; private set; }
        public UserId? GetUserId { get; private set; }
        public OrganizationId? GetUserOrganizationId { get; private set; }

        public Task<OrganizationStorePage<MachineOrganizationSummary, OrganizationListCursorPosition>>
            ListUserOrganizationsAsync(
                UserId userId,
                OrganizationListCursorPosition? after,
                int limit,
                CancellationToken cancellationToken)
        {
            Calls++;
            ListUserId = userId;
            ListAfter = after;
            ListLimit = limit;
            return Task.FromResult(UserOrganizations);
        }

        public Task<MachineOrganizationSummary?> GetUserOrganizationAsync(
            UserId userId,
            OrganizationId organizationId,
            CancellationToken cancellationToken)
        {
            Calls++;
            GetUserId = userId;
            GetUserOrganizationId = organizationId;
            return Task.FromResult(UserOrganization);
        }

        public Task<MachineOrganizationSummary?> GetOrganizationAsync(
            OrganizationId organizationId,
            CancellationToken cancellationToken)
        {
            Calls++;
            GetOrganizationId = organizationId;
            return Task.FromResult(Organization);
        }

        public Task<OrganizationStorePage<OrganizationMember, OrganizationMemberCursorPosition>?>
            ListUserOrganizationMembersAsync(
                UserId userId,
                OrganizationId organizationId,
                OrganizationMemberCursorPosition? after,
                int limit,
                CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(UserMembers);
        }

        public Task<OrganizationStorePage<OrganizationMember, OrganizationMemberCursorPosition>?>
            ListOrganizationMembersAsync(
                OrganizationId organizationId,
                OrganizationMemberCursorPosition? after,
                int limit,
                CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(OrganizationMembers);
        }
    }
}
