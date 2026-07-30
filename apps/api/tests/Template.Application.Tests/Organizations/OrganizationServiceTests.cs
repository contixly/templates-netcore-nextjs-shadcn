using Template.Application.Organizations;
using Template.Application.Organizations.Ports;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Application.Tests.Organizations;

public sealed class OrganizationServiceTests
{
    private static readonly Guid ActorId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SessionId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly OrganizationId OrganizationId =
        new(Guid.Parse("00000000-0000-0000-0000-000000000010"));

    [Fact]
    public async Task Create_normalizes_name_and_passes_actor_and_current_session()
    {
        var expected = OrganizationTestData.Detail();
        var store = new RecordingOrganizationStore
        {
            CreateResult = OrganizationOperationResult<OrganizationDetail>.Success(expected)
        };
        var service = new OrganizationService(store);

        var result = await service.CreateAsync(
            new UserId(ActorId),
            new SessionId(SessionId),
            "  Acme Team  ",
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Same(expected, result.Value);
        Assert.Equal("Acme Team", store.LastCreate!.Name);
        Assert.Equal(new UserId(ActorId), store.LastCreate.ActorUserId);
        Assert.Equal(new SessionId(SessionId), store.LastCreate.SessionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bad.Name")]
    [InlineData("Bad\tName")]
    public async Task Create_rejects_invalid_names_without_store_access(string name)
    {
        var store = new RecordingOrganizationStore();
        var service = new OrganizationService(store);

        var result = await service.CreateAsync(
            new UserId(ActorId),
            new SessionId(SessionId),
            name,
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.InvalidName, result.Failure);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task Invalid_organization_cursor_is_rejected_without_store_access()
    {
        var store = new RecordingOrganizationStore();
        var service = new OrganizationService(store);

        var result = await service.ListAsync(
            new UserId(ActorId),
            "broken",
            50,
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.InvalidCursor, result.Failure);
        Assert.Equal(0, store.ListCalls);
    }

    [Fact]
    public async Task List_decodes_input_and_encodes_the_store_continuation()
    {
        var actor = new UserId(ActorId);
        var after = new OrganizationCursorPosition("ACME TEAM", OrganizationId);
        var next = new OrganizationCursorPosition(
            "BETA TEAM",
            new OrganizationId(Guid.Parse("00000000-0000-0000-0000-000000000011")));
        var store = new RecordingOrganizationStore
        {
            ListResult = new OrganizationStorePage<
                OrganizationSummary,
                OrganizationCursorPosition>([], next)
        };
        var service = new OrganizationService(store);

        var result = await service.ListAsync(
            actor,
            OrganizationCursor.Encode(after),
            25,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(after, store.LastListAfter);
        Assert.Equal(actor, store.LastListActor);
        Assert.Equal(25, store.LastListLimit);
        Assert.NotNull(result.Value!.NextCursor);
        Assert.True(OrganizationCursor.TryDecode(
            result.Value.NextCursor,
            out OrganizationCursorPosition decoded));
        Assert.Equal(next, decoded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task List_rejects_out_of_range_limits(int limit)
    {
        var store = new RecordingOrganizationStore();
        var service = new OrganizationService(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ListAsync(new UserId(ActorId), null, limit, TestContext.Current.CancellationToken));

        Assert.Equal(0, store.ListCalls);
    }

    [Fact]
    public async Task Get_by_key_passes_the_actor()
    {
        var store = new RecordingOrganizationStore();
        var service = new OrganizationService(store);

        await service.GetByKeyAsync(new UserId(ActorId), "acme-team", TestContext.Current.CancellationToken);

        Assert.Equal(new UserId(ActorId), store.LastGetActor);
        Assert.Equal("acme-team", store.LastGetKey);
    }

    [Fact]
    public async Task Update_normalizes_every_optional_value_before_the_store()
    {
        var store = new RecordingOrganizationStore();
        var service = new OrganizationService(store);

        await service.UpdateAsync(
            new UserId(ActorId),
            OrganizationId,
            "  Acme Renamed  ",
            "  New-Slug  ",
            [" Example.COM ", "@example.com", "admin.example.com"],
            TestContext.Current.CancellationToken);

        var command = Assert.IsType<UpdateOrganizationCommand>(store.LastUpdate);
        Assert.Equal(new UserId(ActorId), command.ActorUserId);
        Assert.Equal(OrganizationId, command.OrganizationId);
        Assert.Equal("Acme Renamed", command.Name);
        Assert.Equal("new-slug", command.Slug!.Value.Value);
        Assert.Equal(["example.com", "admin.example.com"], command.AllowedEmailDomains);
    }

    [Fact]
    public async Task Update_rejects_invalid_domain_or_slug_without_store_access()
    {
        var store = new RecordingOrganizationStore();
        var service = new OrganizationService(store);

        var invalidDomain = await service.UpdateAsync(
            new UserId(ActorId),
            OrganizationId,
            null,
            null,
            ["not-a-domain"],
            TestContext.Current.CancellationToken);
        var invalidSlug = await service.UpdateAsync(
            new UserId(ActorId),
            OrganizationId,
            null,
            "bad slug",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.InvalidEmailDomain, invalidDomain.Failure);
        Assert.Equal(OrganizationFailure.InvalidSlug, invalidSlug.Failure);
        Assert.Equal(0, store.UpdateCalls);
    }

    [Fact]
    public async Task Delete_and_set_active_pass_explicit_actor_and_session_context()
    {
        var store = new RecordingOrganizationStore();
        var service = new OrganizationService(store);

        await service.DeleteAsync(
            new UserId(ActorId),
            OrganizationId,
            "Acme Team",
            TestContext.Current.CancellationToken);
        await service.SetActiveAsync(
            new UserId(ActorId),
            new SessionId(SessionId),
            OrganizationId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new DeleteOrganizationCommand(
                new UserId(ActorId),
                OrganizationId,
                "Acme Team"),
            store.LastDelete);
        Assert.Equal(
            new SetActiveOrganizationCommand(
                new UserId(ActorId),
                new SessionId(SessionId),
                OrganizationId),
            store.LastSetActive);
    }
}

internal static class OrganizationTestData
{
    internal static OrganizationDetail Detail()
    {
        Assert.True(OrganizationSlug.TryCreate("acme-team", out var slug));
        return new OrganizationDetail(
            new OrganizationId(Guid.Parse("00000000-0000-0000-0000-000000000010")),
            "Acme Team",
            slug,
            DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
            OrganizationRole.Owner,
            new OrganizationCapabilities(true, true, true, true),
            []);
    }

    internal static OrganizationMember Member() =>
        new(
            new OrganizationMemberId(
                Guid.Parse("00000000-0000-0000-0000-000000000020")),
            new UserId(Guid.Parse("00000000-0000-0000-0000-000000000003")),
            "Target User",
            "target@outside.test",
            null,
            OrganizationRole.Member,
            DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
            "outside.test",
            true);
}

internal sealed class RecordingOrganizationStore : IOrganizationStore
{
    public OrganizationOperationResult<OrganizationDetail> CreateResult { get; set; } =
        OrganizationOperationResult<OrganizationDetail>.Failed(OrganizationFailure.NotFound);

    public OrganizationStorePage<OrganizationSummary, OrganizationCursorPosition> ListResult
    { get; set; } = new([], null);

    public OrganizationStorePage<OrganizationMember, OrganizationMemberCursorPosition>
        ListMembersResult
    { get; set; } = new([], null);

    public OrganizationOperationResult<OrganizationMember> AddMemberResult { get; set; } =
        OrganizationOperationResult<OrganizationMember>.Failed(OrganizationFailure.NotFound);

    public int ListCalls { get; private set; }
    public int CreateCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int ListMemberCalls { get; private set; }
    public UserId? LastListActor { get; private set; }
    public OrganizationCursorPosition? LastListAfter { get; private set; }
    public int? LastListLimit { get; private set; }
    public UserId? LastGetActor { get; private set; }
    public string? LastGetKey { get; private set; }
    public CreateOrganizationCommand? LastCreate { get; private set; }
    public UpdateOrganizationCommand? LastUpdate { get; private set; }
    public DeleteOrganizationCommand? LastDelete { get; private set; }
    public SetActiveOrganizationCommand? LastSetActive { get; private set; }
    public UserId? LastMemberListActor { get; private set; }
    public OrganizationId? LastMemberListOrganizationId { get; private set; }
    public OrganizationMemberCursorPosition? LastMemberListAfter { get; private set; }
    public int? LastMemberListLimit { get; private set; }
    public AddOrganizationMemberCommand? LastAddMember { get; private set; }
    public UpdateOrganizationMemberRoleCommand? LastUpdateRole { get; private set; }

    public Task<OrganizationStorePage<OrganizationSummary, OrganizationCursorPosition>>
        ListAsync(
            UserId actorUserId,
            OrganizationCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        ListCalls++;
        LastListActor = actorUserId;
        LastListAfter = after;
        LastListLimit = limit;
        return Task.FromResult(ListResult);
    }

    public Task<OrganizationOperationResult<OrganizationDetail>> GetByKeyAsync(
        UserId actorUserId,
        string organizationKey,
        CancellationToken cancellationToken)
    {
        LastGetActor = actorUserId;
        LastGetKey = organizationKey;
        return Task.FromResult(
            OrganizationOperationResult<OrganizationDetail>.Failed(
                OrganizationFailure.NotFound));
    }

    public Task<OrganizationOperationResult<OrganizationDetail>> CreateAsync(
        CreateOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        CreateCalls++;
        LastCreate = command;
        return Task.FromResult(CreateResult);
    }

    public Task<OrganizationOperationResult<OrganizationDetail>> UpdateAsync(
        UpdateOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        UpdateCalls++;
        LastUpdate = command;
        return Task.FromResult(
            OrganizationOperationResult<OrganizationDetail>.Failed(
                OrganizationFailure.NotFound));
    }

    public Task<OrganizationOperationResult<OrganizationDeletion>> DeleteAsync(
        DeleteOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        LastDelete = command;
        return Task.FromResult(
            OrganizationOperationResult<OrganizationDeletion>.Failed(
                OrganizationFailure.NotFound));
    }

    public Task<OrganizationOperationResult<ActiveOrganization>> SetActiveAsync(
        SetActiveOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        LastSetActive = command;
        return Task.FromResult(
            OrganizationOperationResult<ActiveOrganization>.Failed(
                OrganizationFailure.NotFound));
    }

    public Task<OrganizationStorePage<OrganizationMember, OrganizationMemberCursorPosition>>
        ListMembersAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        ListMemberCalls++;
        LastMemberListActor = actorUserId;
        LastMemberListOrganizationId = organizationId;
        LastMemberListAfter = after;
        LastMemberListLimit = limit;
        return Task.FromResult(ListMembersResult);
    }

    public Task<OrganizationOperationResult<OrganizationMember>> AddMemberAsync(
        AddOrganizationMemberCommand command,
        CancellationToken cancellationToken)
    {
        LastAddMember = command;
        return Task.FromResult(AddMemberResult);
    }

    public Task<OrganizationOperationResult<OrganizationMember>> UpdateMemberRoleAsync(
        UpdateOrganizationMemberRoleCommand command,
        CancellationToken cancellationToken)
    {
        LastUpdateRole = command;
        return Task.FromResult(
            OrganizationOperationResult<OrganizationMember>.Failed(
                OrganizationFailure.NotFound));
    }
}
