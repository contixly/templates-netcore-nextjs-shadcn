using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;
using Template.Infrastructure.Collaboration;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Collaboration;

public sealed class TeamStoreTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Member_can_read_only_teams_in_their_organization()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("owner@team.test", "Owner");
        var member = await fixture.CreateUserAsync("member@team.test", "Member");
        var foreignOwner = await fixture.CreateUserAsync(
            "foreign-owner@team.test",
            "Foreign Owner");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Readable");
        await fixture.AddOrganizationMemberAsync(
            organization,
            member,
            OrganizationRole.Member);
        var foreignOrganization = await fixture.CreateOrganizationAsync(
            foreignOwner,
            OrganizationRole.Owner,
            "Foreign");
        var expected = await fixture.SeedTeamAsync(
            organization,
            "Дизайн Команда");
        await fixture.SeedTeamMemberAsync(organization, expected, owner);
        await fixture.SeedTeamAsync(foreignOrganization, "Secret");

        var result = await fixture.Store.ListAsync(
            member.UserId,
            organization,
            after: null,
            limit: 50,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        var team = Assert.Single(result.Value!.Items);
        Assert.Equal(expected, team.Id);
        Assert.Equal("Дизайн Команда", team.Name.Value);
        var members = await fixture.Store.ListMembersAsync(
            member.UserId,
            organization,
            expected,
            after: null,
            limit: 50,
            TestContext.Current.CancellationToken);
        var candidates = await fixture.Store.ListCandidatesAsync(
            member.UserId,
            organization,
            expected,
            query: null,
            after: null,
            limit: 50,
            TestContext.Current.CancellationToken);
        Assert.Equal(owner.UserId, Assert.Single(members.Value!.Items).UserId);
        Assert.Equal(member.UserId, Assert.Single(candidates.Value!.Items).UserId);
    }

    [Fact]
    public async Task Member_cannot_mutate_teams()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("denial-owner@team.test");
        var member = await fixture.CreateUserAsync("denial-member@team.test");
        var target = await fixture.CreateUserAsync("denial-target@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Denial");
        await fixture.AddOrganizationMemberAsync(
            organization,
            member,
            OrganizationRole.Member);
        await fixture.AddOrganizationMemberAsync(
            organization,
            target,
            OrganizationRole.Member);
        var team = await fixture.SeedTeamAsync(organization, "Existing");
        await fixture.SeedTeamMemberAsync(organization, team, target);

        var create = await fixture.Store.CreateAsync(
            new(member.UserId, organization, Name("Denied")),
            TestContext.Current.CancellationToken);
        var update = await fixture.Store.UpdateAsync(
            new(member.UserId, organization, team, Name("Renamed")),
            TestContext.Current.CancellationToken);
        var add = await fixture.Store.AddMemberAsync(
            new(member.UserId, organization, team, owner.UserId),
            TestContext.Current.CancellationToken);
        var remove = await fixture.Store.RemoveMemberAsync(
            new(member.UserId, organization, team, target.UserId),
            TestContext.Current.CancellationToken);
        var delete = await fixture.Store.DeleteAsync(
            new(member.UserId, organization, team),
            TestContext.Current.CancellationToken);

        Assert.All(
            new[]
            {
                create.Failure,
                update.Failure,
                add.Failure,
                remove.Failure,
                delete.Failure
            },
            failure => Assert.Equal(TeamFailure.PermissionDenied, failure));
        Assert.Equal(1, await fixture.CountTeamsAsync(organization));
        Assert.Equal(1, await fixture.CountTeamMembersAsync(team));
    }

    [Fact]
    public async Task Owner_and_admin_can_create_update_and_remove_members()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("crud-owner@team.test");
        var admin = await fixture.CreateUserAsync("crud-admin@team.test");
        var target = await fixture.CreateUserAsync(
            "crud-target@team.test",
            "Целевая Участница");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "CRUD");
        await fixture.AddOrganizationMemberAsync(
            organization,
            admin,
            OrganizationRole.Admin);
        await fixture.AddOrganizationMemberAsync(
            organization,
            target,
            OrganizationRole.Member);

        var created = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, Name("Дизайн Команда")),
            TestContext.Current.CancellationToken);
        var team = created.Value!.Id;
        var renamed = await fixture.Store.UpdateAsync(
            new(admin.UserId, organization, team, Name("Инженеры")),
            TestContext.Current.CancellationToken);
        var added = await fixture.Store.AddMemberAsync(
            new(admin.UserId, organization, team, target.UserId),
            TestContext.Current.CancellationToken);
        var removed = await fixture.Store.RemoveMemberAsync(
            new(owner.UserId, organization, team, target.UserId),
            TestContext.Current.CancellationToken);
        var deleted = await fixture.Store.DeleteAsync(
            new(admin.UserId, organization, team),
            TestContext.Current.CancellationToken);

        Assert.True(created.Succeeded);
        Assert.Equal("Дизайн Команда", created.Value.Name.Value);
        Assert.True(renamed.Succeeded);
        Assert.Equal("Инженеры", renamed.Value!.Name.Value);
        Assert.True(added.Succeeded);
        Assert.Equal("Целевая Участница", added.Value!.Name);
        Assert.True(removed.Succeeded);
        Assert.Equal(target.UserId, removed.Value!.UserId);
        Assert.True(deleted.Succeeded);
        Assert.Equal(0, await fixture.CountTeamMembersAsync(team));
        Assert.Equal(0, await fixture.CountTeamsAsync(organization));
    }

    [Fact]
    public async Task Team_names_are_case_insensitively_unique_per_organization()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("name-owner@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Names");
        var first = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, Name("Design")),
            TestContext.Current.CancellationToken);
        var duplicate = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, Name("dESIGN")),
            TestContext.Current.CancellationToken);
        var other = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, Name("Engineering")),
            TestContext.Current.CancellationToken);
        var renameConflict = await fixture.Store.UpdateAsync(
            new(owner.UserId, organization, other.Value!.Id, Name("DESIGN")),
            TestContext.Current.CancellationToken);
        var unchanged = await fixture.Store.UpdateAsync(
            new(owner.UserId, organization, first.Value!.Id, Name("Design")),
            TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded);
        Assert.Equal(TeamFailure.NameConflict, duplicate.Failure);
        Assert.True(other.Succeeded);
        Assert.Equal(TeamFailure.NameConflict, renameConflict.Failure);
        Assert.Equal(TeamFailure.NameUnchanged, unchanged.Failure);
        Assert.Equal(2, await fixture.CountTeamsAsync(organization));
    }

    [Fact]
    public async Task Team_list_uses_stable_positions_and_embeds_first_fifty_members()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("page-owner@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Paging");
        var createdAt = TeamStoreFixture.Now.AddMinutes(1);
        var first = await fixture.SeedTeamAsync(
            organization,
            "First",
            new TeamId(Guid.Parse("10000000-0000-7000-8000-000000000001")),
            createdAt);
        var second = await fixture.SeedTeamAsync(
            organization,
            "Second",
            new TeamId(Guid.Parse("10000000-0000-7000-8000-000000000002")),
            createdAt);
        var third = await fixture.SeedTeamAsync(
            organization,
            "Third",
            new TeamId(Guid.Parse("10000000-0000-7000-8000-000000000003")),
            createdAt);
        for (var index = 0; index < 51; index++)
        {
            var user = await fixture.CreateUserAsync(
                $"page-member-{index:D2}@team.test",
                $"Member {index:D2}");
            await fixture.AddOrganizationMemberAsync(
                organization,
                user,
                OrganizationRole.Member,
                new OrganizationMemberId(Guid.Parse(
                    $"20000000-0000-7000-8000-{index + 1:D12}")),
                TeamStoreFixture.Now.AddMinutes(2));
            await fixture.SeedTeamMemberAsync(
                organization,
                second,
                user,
                new TeamMemberId(Guid.Parse(
                    $"30000000-0000-7000-8000-{index + 1:D12}")),
                TeamStoreFixture.Now.AddMinutes(3));
        }

        var pageOne = await fixture.Store.ListAsync(
            owner.UserId,
            organization,
            after: null,
            limit: 2,
            TestContext.Current.CancellationToken);
        var pageTwo = await fixture.Store.ListAsync(
            owner.UserId,
            organization,
            pageOne.Value!.Next,
            limit: 2,
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { first, second }, pageOne.Value.Items.Select(x => x.Id));
        Assert.Equal(third, Assert.Single(pageTwo.Value!.Items).Id);
        Assert.Null(pageTwo.Value.Next);
        var populated = pageOne.Value.Items[1];
        Assert.Equal(51, populated.MemberCount);
        Assert.Equal(50, populated.Members.Items.Count);
        Assert.Equal(populated.Members.Items[^1].Id, populated.Members.Next!.Id);
        Assert.Equal(populated.Members.Items[^1].TeamJoinedAt, populated.Members.Next.JoinedAt);
    }

    [Fact]
    public async Task Member_pages_are_stable_for_equal_join_times()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("member-page-owner@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Member Paging");
        var team = await fixture.SeedTeamAsync(organization, "Paging Team");
        var expected = new List<TeamMemberId>();
        for (var index = 0; index < 3; index++)
        {
            var user = await fixture.CreateUserAsync(
                $"stable-member-{index}@team.test");
            await fixture.AddOrganizationMemberAsync(
                organization,
                user,
                OrganizationRole.Member);
            var id = new TeamMemberId(Guid.Parse(
                $"40000000-0000-7000-8000-{index + 1:D12}"));
            expected.Add(id);
            await fixture.SeedTeamMemberAsync(
                organization,
                team,
                user,
                id,
                TeamStoreFixture.Now.AddHours(1));
        }

        var first = await fixture.Store.ListMembersAsync(
            owner.UserId,
            organization,
            team,
            after: null,
            limit: 2,
            TestContext.Current.CancellationToken);
        var second = await fixture.Store.ListMembersAsync(
            owner.UserId,
            organization,
            team,
            first.Value!.Next,
            limit: 2,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected[..2], first.Value.Items.Select(row => row.Id));
        Assert.Equal(expected[2], Assert.Single(second.Value!.Items).Id);
        Assert.Null(second.Value.Next);
    }

    [Fact]
    public async Task Candidate_search_is_trimmed_case_insensitive_tenant_scoped_and_excludes_members()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("candidate-owner@team.test");
        var current = await fixture.CreateUserAsync(
            "pat.current@team.test",
            "Pat Current");
        var nameMatch = await fixture.CreateUserAsync(
            "name@team.test",
            "PAT Candidate");
        var emailMatch = await fixture.CreateUserAsync(
            "email.PAT@team.test",
            "Email Candidate");
        var foreign = await fixture.CreateUserAsync(
            "foreign.pat@team.test",
            "Foreign Pat");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Candidates");
        var foreignOrganization = await fixture.CreateOrganizationAsync(
            foreign,
            OrganizationRole.Owner,
            "Foreign Candidates");
        foreach (var candidate in new[] { current, nameMatch, emailMatch })
        {
            await fixture.AddOrganizationMemberAsync(
                organization,
                candidate,
                OrganizationRole.Member);
        }

        var team = await fixture.SeedTeamAsync(organization, "Search");
        await fixture.SeedTeamMemberAsync(organization, team, current);

        var result = await fixture.Store.ListCandidatesAsync(
            owner.UserId,
            organization,
            team,
            "  pat  ",
            after: null,
            limit: 1,
            TestContext.Current.CancellationToken);
        var continuation = await fixture.Store.ListCandidatesAsync(
            owner.UserId,
            organization,
            team,
            "  pat  ",
            result.Value!.Next,
            limit: 1,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value.Next);
        Assert.Null(continuation.Value!.Next);
        Assert.Equal(
            new[] { nameMatch.UserId, emailMatch.UserId }
                .OrderBy(userId => userId.Value),
            result.Value.Items.Concat(continuation.Value.Items)
                .Select(candidate => candidate.UserId)
                .OrderBy(userId => userId.Value));
        Assert.DoesNotContain(
            result.Value.Items.Concat(continuation.Value.Items),
            candidate => candidate.UserId == current.UserId ||
                         candidate.UserId == foreign.UserId);
        Assert.NotEqual(default, foreignOrganization);
    }

    [Fact]
    public async Task Cross_organization_membership_is_rejected_without_a_write()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var ownerA = await fixture.CreateUserAsync("cross-owner-a@team.test");
        var ownerB = await fixture.CreateUserAsync("cross-owner-b@team.test");
        var organizationA = await fixture.CreateOrganizationAsync(
            ownerA,
            OrganizationRole.Owner,
            "A");
        await fixture.CreateOrganizationAsync(
            ownerB,
            OrganizationRole.Owner,
            "B");
        var teamA = await fixture.SeedTeamAsync(organizationA, "A Team");

        var result = await fixture.Store.AddMemberAsync(
            new(ownerA.UserId, organizationA, teamA, ownerB.UserId),
            TestContext.Current.CancellationToken);

        Assert.Equal(TeamFailure.MemberNotFound, result.Failure);
        Assert.Equal(0, await fixture.CountTeamMembersAsync(teamA));
    }

    [Fact]
    public async Task Duplicate_team_membership_is_classified_without_a_second_write()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("duplicate-owner@team.test");
        var target = await fixture.CreateUserAsync("duplicate-target@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Duplicates");
        await fixture.AddOrganizationMemberAsync(
            organization,
            target,
            OrganizationRole.Member);
        var team = await fixture.SeedTeamAsync(organization, "Duplicate Team");

        var first = await fixture.Store.AddMemberAsync(
            new(owner.UserId, organization, team, target.UserId),
            TestContext.Current.CancellationToken);
        var duplicate = await fixture.Store.AddMemberAsync(
            new(owner.UserId, organization, team, target.UserId),
            TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded);
        Assert.Equal(TeamFailure.MemberAlreadyExists, duplicate.Failure);
        Assert.Equal(1, await fixture.CountTeamMembersAsync(team));
    }

    [Fact]
    public async Task Delete_clears_invitation_team_target_before_deleting_the_team()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("delete-owner@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Delete");
        var team = await fixture.SeedTeamAsync(organization, "Delete Team");
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            team,
            owner,
            "invitee@team.test");

        var result = await fixture.Store.DeleteAsync(
            new(owner.UserId, organization, team),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(team, result.Value!.TeamId);
        await using var db = fixture.CreateDbContext();
        Assert.Null((await db.Invitations.SingleAsync(
            row => row.Id == invitation,
            TestContext.Current.CancellationToken)).TeamId);
        Assert.False(await db.Teams.AnyAsync(
            row => row.Id == team.Value,
            TestContext.Current.CancellationToken));
    }

    private static TeamName Name(string value)
    {
        Assert.True(TeamName.TryCreate(value, out var name));
        return name;
    }
}

internal sealed class TeamStoreFixture : IAsyncDisposable
{
    internal static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainerFixture _postgres;
    private readonly string _databaseName;
    private readonly string _connectionString;
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _storeScope;

    private TeamStoreFixture(
        PostgreSqlContainerFixture postgres,
        string databaseName,
        string connectionString,
        ServiceProvider services)
    {
        _postgres = postgres;
        _databaseName = databaseName;
        _connectionString = connectionString;
        _services = services;
        _storeScope = services.CreateAsyncScope();
    }

    internal ITeamStore Store =>
        _storeScope.ServiceProvider.GetRequiredService<ITeamStore>();

    internal static async Task<TeamStoreFixture> CreateAsync(
        PostgreSqlContainerFixture postgres)
    {
        var database = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = database.ConnectionString,
                ["DataProtection:ApplicationName"] = "Template"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<TimeProvider>(new FixedTeamTimeProvider(Now));
        services.AddSingleton<TeamMutationStartBarrier>();
        services.AddDbContext<TemplateDbContext>((provider, options) =>
            options.AddInterceptors(
                provider.GetRequiredService<TeamMutationStartBarrier>()));
        services.AddAuthInfrastructure(configuration, new TestHostEnvironment());
        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<TemplateDbContext>()
                .Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return new TeamStoreFixture(
            postgres,
            database.DatabaseName,
            database.ConnectionString,
            provider);
    }

    internal TemplateDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        TemplateDbContext.Configure(options, _connectionString);
        return new TemplateDbContext(options.Options);
    }

    internal void CoordinateNextMutationPair() =>
        _services.GetRequiredService<TeamMutationStartBarrier>()
            .CoordinateNextPair();

    internal bool MutationPairWasCoordinated =>
        _services.GetRequiredService<TeamMutationStartBarrier>()
            .WasCoordinated;

    internal async Task<TeamOperationResult<TeamSummary>> CreateTeamAsync(
        TeamActor actor,
        OrganizationId organizationId,
        string name)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITeamStore>()
            .CreateAsync(
                new(actor.UserId, organizationId, TeamNameFrom(name)),
                TestContext.Current.CancellationToken);
    }

    internal async Task<TeamOperationResult<TeamSummary>> UpdateTeamAsync(
        TeamActor actor,
        OrganizationId organizationId,
        TeamId teamId,
        string name)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITeamStore>()
            .UpdateAsync(
                new(actor.UserId, organizationId, teamId, TeamNameFrom(name)),
                TestContext.Current.CancellationToken);
    }

    internal async Task<TeamOperationResult<TeamDeletion>> DeleteTeamAsync(
        TeamActor actor,
        OrganizationId organizationId,
        TeamId teamId)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITeamStore>()
            .DeleteAsync(
                new(actor.UserId, organizationId, teamId),
                TestContext.Current.CancellationToken);
    }

    internal async Task<TeamOperationResult<TeamMemberView>>
        AddTeamMemberAsync(
            TeamActor actor,
            OrganizationId organizationId,
            TeamId teamId,
            TeamActor target)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITeamStore>()
            .AddMemberAsync(
                new(actor.UserId, organizationId, teamId, target.UserId),
                TestContext.Current.CancellationToken);
    }

    private static TeamName TeamNameFrom(string value)
    {
        if (!TeamName.TryCreate(value, out var name))
        {
            throw new ArgumentException("The test team name is invalid.", nameof(value));
        }

        return name;
    }

    internal async Task<TeamActor> CreateUserAsync(
        string email,
        string? displayName = null)
    {
        var userId = Guid.CreateVersion7(Now);
        await using var db = CreateDbContext();
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = displayName ?? email.Split('@')[0],
            IsLocalAutomation = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            CreatedAt = Now,
            UpdatedAt = Now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new TeamActor(new UserId(userId));
    }

    internal async Task<OrganizationId> CreateOrganizationAsync(
        TeamActor actor,
        OrganizationRole role,
        string name)
    {
        var organizationId = OrganizationId.New();
        await using var db = CreateDbContext();
        db.Organizations.Add(new OrganizationEntity
        {
            Id = organizationId.Value,
            Name = name,
            Slug = $"team-{Guid.NewGuid():N}",
            CreatedAt = Now,
            UpdatedAt = Now
        });
        db.OrganizationMembers.Add(new OrganizationMemberEntity
        {
            Id = OrganizationMemberId.New().Value,
            OrganizationId = organizationId.Value,
            UserId = actor.UserId.Value,
            Role = role.Value,
            JoinedAt = Now,
            UpdatedAt = Now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return organizationId;
    }

    internal async Task<OrganizationMemberId> AddOrganizationMemberAsync(
        OrganizationId organizationId,
        TeamActor actor,
        OrganizationRole role,
        OrganizationMemberId? memberId = null,
        DateTimeOffset? joinedAt = null)
    {
        var id = memberId ?? OrganizationMemberId.New();
        await using var db = CreateDbContext();
        db.OrganizationMembers.Add(new OrganizationMemberEntity
        {
            Id = id.Value,
            OrganizationId = organizationId.Value,
            UserId = actor.UserId.Value,
            Role = role.Value,
            JoinedAt = joinedAt ?? Now,
            UpdatedAt = joinedAt ?? Now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    internal async Task<TeamId> SeedTeamAsync(
        OrganizationId organizationId,
        string name,
        TeamId? teamId = null,
        DateTimeOffset? createdAt = null)
    {
        var id = teamId ?? TeamId.New(Now);
        var timestamp = createdAt ?? Now;
        await using var db = CreateDbContext();
        db.Teams.Add(new TeamEntity
        {
            Id = id.Value,
            OrganizationId = organizationId.Value,
            Name = name,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    internal async Task<TeamMemberId> SeedTeamMemberAsync(
        OrganizationId organizationId,
        TeamId teamId,
        TeamActor actor,
        TeamMemberId? teamMemberId = null,
        DateTimeOffset? joinedAt = null)
    {
        await using var db = CreateDbContext();
        var organizationMemberId = await db.OrganizationMembers
            .Where(row =>
                row.OrganizationId == organizationId.Value &&
                row.UserId == actor.UserId.Value)
            .Select(row => row.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        var id = teamMemberId ?? TeamMemberId.New(Now);
        db.TeamMembers.Add(new TeamMemberEntity
        {
            Id = id.Value,
            OrganizationId = organizationId.Value,
            TeamId = teamId.Value,
            OrganizationMemberId = organizationMemberId,
            JoinedAt = joinedAt ?? Now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    internal async Task<Guid> SeedInvitationAsync(
        OrganizationId organizationId,
        TeamId teamId,
        TeamActor inviter,
        string email)
    {
        var id = Guid.NewGuid();
        await using var db = CreateDbContext();
        db.Invitations.Add(new InvitationEntity
        {
            Id = id,
            OrganizationId = organizationId.Value,
            TeamId = teamId.Value,
            Email = email,
            Role = OrganizationRole.Member.Value,
            Status = "pending",
            InviterUserId = inviter.UserId.Value,
            CreatedAt = Now,
            UpdatedAt = Now,
            ExpiresAt = Now.AddDays(2)
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    internal async Task<int> CountTeamsAsync(OrganizationId organizationId)
    {
        await using var db = CreateDbContext();
        return await db.Teams.CountAsync(
            row => row.OrganizationId == organizationId.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<int> CountTeamMembersAsync(TeamId teamId)
    {
        await using var db = CreateDbContext();
        return await db.TeamMembers.CountAsync(
            row => row.TeamId == teamId.Value,
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _storeScope.DisposeAsync();
        await _services.DisposeAsync();
        await _postgres.DropDatabaseAsync(
            _databaseName,
            TestContext.Current.CancellationToken);
    }
}

internal sealed record TeamActor(UserId UserId);

internal sealed class FixedTeamTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class TeamMutationStartBarrier : DbCommandInterceptor
{
    private int _enabled;
    private int _arrivals;
    private TaskCompletionSource _release = NewSignal();

    internal bool WasCoordinated => Volatile.Read(ref _arrivals) == 2;

    internal void CoordinateNextPair()
    {
        _arrivals = 0;
        _release = NewSignal();
        Volatile.Write(ref _enabled, 1);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>>
        ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _enabled) == 0 ||
            !command.CommandText.Contains(
                "FROM organizations.organizations",
                StringComparison.OrdinalIgnoreCase) ||
            !command.CommandText.Contains(
                "FOR UPDATE",
                StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        if (Interlocked.Increment(ref _arrivals) == 2)
        {
            Volatile.Write(ref _enabled, 0);
            _release.TrySetResult();
        }

        await _release.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);
        return result;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
