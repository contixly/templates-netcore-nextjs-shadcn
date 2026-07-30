using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Template.Api.Tests.Infrastructure;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Application.Organizations;
using Template.Application.Organizations.Ports;
using Template.Domain.Authentication;
using Template.Domain.Organizations;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Organizations;

public sealed class OrganizationStoreTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Create_stores_owner_and_active_session_atomically()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "owner@local-agent.test");

        var result = await fixture.Store.CreateAsync(
            new CreateOrganizationCommand(
                actor.UserId,
                actor.SessionId,
                "Acme"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        var detail = Assert.IsType<OrganizationDetail>(result.Value);
        Assert.Equal("acme", detail.Slug.Value);
        Assert.Equal(OrganizationRole.Owner, detail.CurrentRole);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(
            OrganizationRole.Owner.Value,
            await db.OrganizationMembers
                .Where(row => row.OrganizationId == detail.Id.Value)
                .Select(row => row.Role)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            detail.Id.Value,
            await db.Sessions
                .Where(row => row.Id == actor.SessionId.Value)
                .Select(row => row.ActiveOrganizationId)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_rolls_back_organization_when_session_is_not_current()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "missing-session@local-agent.test");

        var result = await fixture.Store.CreateAsync(
            new CreateOrganizationCommand(
                actor.UserId,
                SessionId.New(),
                "Atomic"),
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.NotFound, result.Failure);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Organizations.AnyAsync(
            TestContext.Current.CancellationToken));
        Assert.False(await db.OrganizationMembers.AnyAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Organization_pages_are_tenant_qualified_and_ordered_by_name_then_id()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "reader@local-agent.test");
        var foreign = await fixture.CreateUserAndSessionAsync(
            "foreign@local-agent.test");
        var alpha1 = new OrganizationId(
            Guid.Parse("00000000-0000-0000-0000-000000000011"));
        var alpha2 = new OrganizationId(
            Guid.Parse("00000000-0000-0000-0000-000000000012"));
        var bravo = new OrganizationId(
            Guid.Parse("00000000-0000-0000-0000-000000000013"));
        await fixture.SeedOrganizationForAsync(
            actor,
            "alpha",
            "alpha-one",
            OrganizationRole.Member,
            alpha1);
        await fixture.SeedOrganizationForAsync(
            actor,
            "Alpha",
            "alpha-two",
            OrganizationRole.Admin,
            alpha2);
        await fixture.SeedOrganizationForAsync(
            actor,
            "Bravo",
            "bravo",
            OrganizationRole.Owner,
            bravo);
        var hidden = await fixture.SeedOrganizationForAsync(
            foreign,
            "Aardvark",
            "hidden",
            OrganizationRole.Owner);

        var first = await fixture.Store.ListAsync(
            actor.UserId,
            after: null,
            limit: 2,
            TestContext.Current.CancellationToken);
        var second = await fixture.Store.ListAsync(
            actor.UserId,
            Assert.IsType<OrganizationCursorPosition>(first.Next),
            limit: 2,
            TestContext.Current.CancellationToken);

        Assert.Equal([alpha1, alpha2], first.Items.Select(item => item.Id));
        Assert.Equal([bravo], second.Items.Select(item => item.Id));
        Assert.Null(second.Next);
        Assert.DoesNotContain(
            first.Items.Concat(second.Items),
            item => item.Id == hidden);

        var bySlug = await fixture.Store.GetByKeyAsync(
            actor.UserId,
            "hidden",
            TestContext.Current.CancellationToken);
        var byId = await fixture.Store.GetByKeyAsync(
            actor.UserId,
            hidden.Value.ToString(),
            TestContext.Current.CancellationToken);
        Assert.Equal(OrganizationFailure.NotFound, bySlug.Failure);
        Assert.Equal(OrganizationFailure.NotFound, byId.Failure);
    }

    [Fact]
    public async Task Uuid_shaped_slug_resolution_prefers_an_accessible_id_then_an_accessible_slug()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "uuid-slug-owner@local-agent.test");
        var foreign = await fixture.CreateUserAndSessionAsync(
            "uuid-slug-foreign@local-agent.test");
        var ambiguousKey =
            Guid.Parse("00000000-0000-0000-0000-000000000041");
        var idMatch = await fixture.SeedOrganizationForAsync(
            actor,
            "ID Match",
            "id-match",
            OrganizationRole.Owner,
            new OrganizationId(ambiguousKey));
        await fixture.SeedOrganizationForAsync(
            actor,
            "Slug Match",
            ambiguousKey.ToString(),
            OrganizationRole.Owner);
        var foreignId = Guid.Parse(
            "00000000-0000-0000-0000-000000000042");
        await fixture.SeedOrganizationForAsync(
            foreign,
            "Foreign ID",
            "foreign-id",
            OrganizationRole.Owner,
            new OrganizationId(foreignId));
        var accessibleSlug = await fixture.SeedOrganizationForAsync(
            actor,
            "Accessible Slug",
            foreignId.ToString(),
            OrganizationRole.Owner);

        var idWins = await fixture.Store.GetByKeyAsync(
            actor.UserId,
            ambiguousKey.ToString(),
            TestContext.Current.CancellationToken);
        var membershipQualifiedSlug = await fixture.Store.GetByKeyAsync(
            actor.UserId,
            foreignId.ToString(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            idMatch,
            Assert.IsType<OrganizationDetail>(idWins.Value).Id);
        Assert.Equal(
            accessibleSlug,
            Assert.IsType<OrganizationDetail>(
                membershipQualifiedSlug.Value).Id);
    }

    [Fact]
    public async Task Create_uses_slug_suffix_and_rejects_accessible_name_duplicate()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "slug-owner@local-agent.test");
        var foreign = await fixture.CreateUserAndSessionAsync(
            "slug-foreign@local-agent.test");
        await fixture.SeedOrganizationForAsync(
            foreign,
            "Foreign",
            "acme",
            OrganizationRole.Owner);

        var created = await fixture.Store.CreateAsync(
            new CreateOrganizationCommand(
                actor.UserId,
                actor.SessionId,
                "Acme"),
            TestContext.Current.CancellationToken);
        var duplicate = await fixture.Store.CreateAsync(
            new CreateOrganizationCommand(
                actor.UserId,
                actor.SessionId,
                "aCME"),
            TestContext.Current.CancellationToken);

        Assert.Equal("acme-2", Assert.IsType<OrganizationDetail>(created.Value).Slug.Value);
        Assert.Equal(OrganizationFailure.NameConflict, duplicate.Failure);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(
            1,
            await db.OrganizationMembers.CountAsync(
                row => row.UserId == actor.UserId.Value,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Name_conflicts_are_culture_independent()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "culture-owner@local-agent.test");
        await fixture.SeedOrganizationForAsync(
            actor,
            "Istanbul",
            "existing-istanbul",
            OrganizationRole.Owner);
        var renameTarget = await fixture.SeedOrganizationForAsync(
            actor,
            "Rename Target",
            "rename-target",
            OrganizationRole.Owner);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            var create = await fixture.Store.CreateAsync(
                new CreateOrganizationCommand(
                    actor.UserId,
                    actor.SessionId,
                    "ISTANBUL"),
                TestContext.Current.CancellationToken);
            var update = await fixture.Store.UpdateAsync(
                new UpdateOrganizationCommand(
                    actor.UserId,
                    renameTarget,
                    "ISTANBUL",
                    Slug: null,
                    AllowedEmailDomains: null),
                TestContext.Current.CancellationToken);

            Assert.Equal(OrganizationFailure.NameConflict, create.Failure);
            Assert.Equal(OrganizationFailure.NameConflict, update.Failure);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public async Task Create_uses_postgres_unicode_name_normalization_for_both_operands()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "unicode-create-owner@local-agent.test");
        await fixture.SeedOrganizationForAsync(
            actor,
            "istanbul",
            "existing-unicode-create",
            OrganizationRole.Owner);
        const string unicodeInput = "İSTANBUL";
        var databaseLower = await fixture.LowerInPostgresAsync(unicodeInput);

        Assert.NotEqual(unicodeInput.ToLowerInvariant(), databaseLower);
        Assert.Equal(
            await fixture.LowerInPostgresAsync("istanbul"),
            databaseLower);

        var result = await fixture.Store.CreateAsync(
            new CreateOrganizationCommand(
                actor.UserId,
                actor.SessionId,
                unicodeInput),
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.NameConflict, result.Failure);
        Assert.Equal(1, await fixture.CountAccessibleOrganizationsAsync(actor));
    }

    [Fact]
    public async Task Update_uses_postgres_unicode_name_normalization_for_both_operands()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "unicode-update-owner@local-agent.test");
        await fixture.SeedOrganizationForAsync(
            actor,
            "istanbul",
            "existing-unicode-update",
            OrganizationRole.Owner);
        var target = await fixture.SeedOrganizationForAsync(
            actor,
            "Rename Unicode Target",
            "rename-unicode-target",
            OrganizationRole.Owner);
        const string unicodeInput = "İSTANBUL";
        var databaseLower = await fixture.LowerInPostgresAsync(unicodeInput);

        Assert.NotEqual(unicodeInput.ToLowerInvariant(), databaseLower);
        Assert.Equal(
            await fixture.LowerInPostgresAsync("istanbul"),
            databaseLower);

        var result = await fixture.Store.UpdateAsync(
            new UpdateOrganizationCommand(
                actor.UserId,
                target,
                unicodeInput,
                Slug: null,
                AllowedEmailDomains: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.NameConflict, result.Failure);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(
            "Rename Unicode Target",
            await db.Organizations
                .Where(row => row.Id == target.Value)
                .Select(row => row.Name)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_replaces_domains_and_checks_permission_before_conflicts()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAndSessionAsync(
            "update-owner@local-agent.test");
        var member = await fixture.CreateUserAndSessionAsync(
            "update-member@local-agent.test");
        var organizationId = await fixture.SeedOrganizationForAsync(
            owner,
            "Original",
            "original",
            OrganizationRole.Owner);
        await fixture.AddMembershipAsync(
            organizationId,
            member,
            OrganizationRole.Member);
        await fixture.SeedAllowedDomainsAsync(organizationId, "old.example");
        await fixture.SeedOrganizationForAsync(
            owner,
            "Conflict",
            "conflict",
            OrganizationRole.Owner);

        var denied = await fixture.Store.UpdateAsync(
            new UpdateOrganizationCommand(
                member.UserId,
                organizationId,
                "Conflict",
                OrganizationSlugForTest("conflict"),
                ["denied.example"]),
            TestContext.Current.CancellationToken);
        var updated = await fixture.Store.UpdateAsync(
            new UpdateOrganizationCommand(
                owner.UserId,
                organizationId,
                "Updated",
                OrganizationSlugForTest("updated"),
                ["z.example", "a.example"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.PermissionDenied, denied.Failure);
        var detail = Assert.IsType<OrganizationDetail>(updated.Value);
        Assert.Equal("Updated", detail.Name);
        Assert.Equal("updated", detail.Slug.Value);
        Assert.Equal(["a.example", "z.example"], detail.AllowedEmailDomains);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(
            ["a.example", "z.example"],
            await db.OrganizationAllowedEmailDomains
                .Where(row => row.OrganizationId == organizationId.Value)
                .OrderBy(row => row.Domain)
                .Select(row => row.Domain)
                .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_requires_owner_exact_confirmation_and_an_accessible_fallback()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAndSessionAsync(
            "delete-owner@local-agent.test");
        var first = await fixture.SeedOrganizationForAsync(
            owner,
            "Delete Me",
            "delete-me",
            OrganizationRole.Owner);

        var last = await fixture.Store.DeleteAsync(
            new DeleteOrganizationCommand(owner.UserId, first, "Delete Me"),
            TestContext.Current.CancellationToken);
        Assert.Equal(OrganizationFailure.LastAccessibleOrganization, last.Failure);

        var fallback = await fixture.SeedOrganizationForAsync(
            owner,
            "Fallback",
            "fallback",
            OrganizationRole.Owner);
        await fixture.SetSessionActiveAsync(owner.SessionId, first);
        var mismatch = await fixture.Store.DeleteAsync(
            new DeleteOrganizationCommand(owner.UserId, first, "delete me"),
            TestContext.Current.CancellationToken);
        var deleted = await fixture.Store.DeleteAsync(
            new DeleteOrganizationCommand(owner.UserId, first, "Delete Me"),
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.ConfirmationMismatch, mismatch.Failure);
        Assert.Equal(first, Assert.IsType<OrganizationDeletion>(deleted.Value).OrganizationId);
        await using var db = fixture.CreateDbContext();
        Assert.Null(await db.Sessions
            .Where(row => row.Id == owner.SessionId.Value)
            .Select(row => row.ActiveOrganizationId)
            .SingleAsync(TestContext.Current.CancellationToken));
        var page = await fixture.Store.ListAsync(
            owner.UserId,
            after: null,
            limit: 50,
            TestContext.Current.CancellationToken);
        Assert.Equal(fallback, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Member_pages_are_ordered_by_joined_at_then_id()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAndSessionAsync(
            "members-owner@local-agent.test");
        var firstUser = await fixture.CreateUserAndSessionAsync(
            "members-first@local-agent.test");
        var secondUser = await fixture.CreateUserAndSessionAsync(
            "members-second@local-agent.test");
        var organizationId = await fixture.SeedOrganizationForAsync(
            owner,
            "Members",
            "members",
            OrganizationRole.Owner,
            memberId: new OrganizationMemberId(
                Guid.Parse("00000000-0000-0000-0000-000000000021")),
            joinedAt: OrganizationStoreFixture.Now);
        await fixture.AddMembershipAsync(
            organizationId,
            firstUser,
            OrganizationRole.Member,
            new OrganizationMemberId(
                Guid.Parse("00000000-0000-0000-0000-000000000022")),
            OrganizationStoreFixture.Now);
        await fixture.AddMembershipAsync(
            organizationId,
            secondUser,
            OrganizationRole.Admin,
            new OrganizationMemberId(
                Guid.Parse("00000000-0000-0000-0000-000000000023")),
            OrganizationStoreFixture.Now.AddMinutes(1));

        var first = await fixture.Store.ListMembersAsync(
            owner.UserId,
            organizationId,
            after: null,
            limit: 2,
            TestContext.Current.CancellationToken);
        var second = await fixture.Store.ListMembersAsync(
            owner.UserId,
            organizationId,
            Assert.IsType<OrganizationMemberCursorPosition>(
                Assert.IsType<OrganizationStorePage<
                    OrganizationMember,
                    OrganizationMemberCursorPosition>>(first.Value).Next),
            limit: 2,
            TestContext.Current.CancellationToken);
        var firstPage = Assert.IsType<OrganizationStorePage<
            OrganizationMember,
            OrganizationMemberCursorPosition>>(first.Value);
        var secondPage = Assert.IsType<OrganizationStorePage<
            OrganizationMember,
            OrganizationMemberCursorPosition>>(second.Value);

        Assert.Equal(
            [
                Guid.Parse("00000000-0000-0000-0000-000000000021"),
                Guid.Parse("00000000-0000-0000-0000-000000000022")
            ],
            firstPage.Items.Select(item => item.Id.Value));
        Assert.Equal(
            Guid.Parse("00000000-0000-0000-0000-000000000023"),
            Assert.Single(secondPage.Items).Id.Value);
        Assert.Null(secondPage.Next);

        var foreign = await fixture.CreateUserAndSessionAsync(
            "members-foreign@local-agent.test");
        var hidden = await fixture.Store.ListMembersAsync(
            foreign.UserId,
            organizationId,
            after: null,
            limit: 50,
            TestContext.Current.CancellationToken);
        Assert.Equal(OrganizationFailure.NotFound, hidden.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Member_list_uses_exact_id_when_accessible_slug_looks_like_uuid(
        bool idBelongsToForeignOrganization)
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "exact-member-list@local-agent.test");
        var requestedId = OrganizationId.New();
        await fixture.SeedOrganizationForAsync(
            actor,
            "Accessible UUID Slug",
            requestedId.Value.ToString("D"),
            OrganizationRole.Owner);
        if (idBelongsToForeignOrganization)
        {
            var foreign = await fixture.CreateUserAndSessionAsync(
                "exact-member-list-foreign@local-agent.test");
            await fixture.SeedOrganizationForAsync(
                foreign,
                "Foreign UUID Target",
                "foreign-uuid-target",
                OrganizationRole.Owner,
                requestedId);
        }

        var result = await fixture.Store.ListMembersAsync(
            actor.UserId,
            requestedId,
            after: null,
            limit: 50,
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.NotFound, result.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Member_list_returns_not_found_when_target_or_access_disappears(
        bool deleteOrganization)
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "disappearing-member-list@local-agent.test");
        var organizationId = await fixture.SeedOrganizationForAsync(
            actor,
            "Disappearing Member List",
            "disappearing-member-list",
            OrganizationRole.Owner);
        await using var connection = new NpgsqlConnection(
            fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = deleteOrganization
                ? """
                  SELECT id
                  FROM organizations.organizations
                  WHERE id = $1
                  FOR UPDATE
                  """
                : """
                  SELECT id
                  FROM organizations.members
                  WHERE organization_id = $1 AND user_id = $2
                  FOR UPDATE
                  """;
            lockCommand.Parameters.AddWithValue(organizationId.Value);
            if (!deleteOrganization)
            {
                lockCommand.Parameters.AddWithValue(actor.UserId.Value);
            }

            Assert.NotNull(await lockCommand.ExecuteScalarAsync(
                TestContext.Current.CancellationToken));
        }

        var listing = fixture.Store.ListMembersAsync(
            actor.UserId,
            organizationId,
            after: null,
            limit: 50,
            TestContext.Current.CancellationToken);
        await Task.Delay(
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken);
        Assert.False(listing.IsCompleted);
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = deleteOrganization
                ? "DELETE FROM organizations.organizations WHERE id = $1"
                : """
                  DELETE FROM organizations.members
                  WHERE organization_id = $1 AND user_id = $2
                  """;
            deleteCommand.Parameters.AddWithValue(organizationId.Value);
            if (!deleteOrganization)
            {
                deleteCommand.Parameters.AddWithValue(actor.UserId.Value);
            }

            Assert.Equal(
                1,
                await deleteCommand.ExecuteNonQueryAsync(
                    TestContext.Current.CancellationToken));
        }

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        var result = await listing;

        Assert.Equal(OrganizationFailure.NotFound, result.Failure);
    }

    [Fact]
    public async Task Add_member_requires_domain_acknowledgement_without_writing_then_retries_once()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAndSessionAsync(
            "domain-owner@allowed.example");
        var target = await fixture.CreateUserAndSessionAsync(
            "target@outside.example",
            "Outside User");
        var organizationId = await fixture.SeedOrganizationForAsync(
            owner,
            "Domain",
            "domain",
            OrganizationRole.Owner);
        await fixture.SeedAllowedDomainsAsync(
            organizationId,
            "allowed.example");
        var command = new AddOrganizationMemberCommand(
            owner.UserId,
            organizationId,
            target.UserId,
            OrganizationRole.Member,
            AcknowledgeDomainRestriction: false);

        var warning = await fixture.Store.AddMemberAsync(
            command,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            OrganizationFailure.DomainAcknowledgementRequired,
            warning.Failure);
        var acknowledgement = Assert.IsType<OrganizationDomainAcknowledgement>(
            warning.Acknowledgement);
        Assert.Equal("target@outside.example", acknowledgement.Email);
        Assert.Equal("outside.example", acknowledgement.EmailDomain);
        Assert.Equal(["allowed.example"], acknowledgement.AllowedEmailDomains);
        Assert.Equal(
            0,
            await fixture.CountMembershipsAsync(organizationId, target.UserId));

        var added = await fixture.Store.AddMemberAsync(
            command with { AcknowledgeDomainRestriction = true },
            TestContext.Current.CancellationToken);
        var duplicateRetry = await fixture.Store.AddMemberAsync(
            command with { AcknowledgeDomainRestriction = true },
            TestContext.Current.CancellationToken);

        Assert.True(added.Succeeded);
        Assert.True(Assert.IsType<OrganizationMember>(added.Value)
            .IsOutsideAllowedEmailDomains);
        Assert.Equal(OrganizationFailure.MemberAlreadyExists, duplicateRetry.Failure);
        Assert.Equal(
            1,
            await fixture.CountMembershipsAsync(organizationId, target.UserId));
    }

    [Fact]
    public async Task Membership_writes_apply_authorization_before_target_disclosure_and_forbid_self_edits()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAndSessionAsync(
            "roles-owner@local-agent.test");
        var admin = await fixture.CreateUserAndSessionAsync(
            "roles-admin@local-agent.test");
        var member = await fixture.CreateUserAndSessionAsync(
            "roles-member@local-agent.test");
        var target = await fixture.CreateUserAndSessionAsync(
            "roles-target@local-agent.test");
        var organizationId = await fixture.SeedOrganizationForAsync(
            owner,
            "Roles",
            "roles",
            OrganizationRole.Owner);
        var adminMemberId = await fixture.AddMembershipAsync(
            organizationId,
            admin,
            OrganizationRole.Admin);
        await fixture.AddMembershipAsync(
            organizationId,
            member,
            OrganizationRole.Member);
        var ownerMemberId = await fixture.FindMemberIdAsync(
            organizationId,
            owner.UserId);

        var hiddenTarget = await fixture.Store.AddMemberAsync(
            new AddOrganizationMemberCommand(
                member.UserId,
                organizationId,
                UserId.New(),
                OrganizationRole.Member,
                AcknowledgeDomainRestriction: true),
            TestContext.Current.CancellationToken);
        var adminCannotCreateOwner = await fixture.Store.AddMemberAsync(
            new AddOrganizationMemberCommand(
                admin.UserId,
                organizationId,
                target.UserId,
                OrganizationRole.Owner,
                AcknowledgeDomainRestriction: true),
            TestContext.Current.CancellationToken);
        var adminCannotDemoteOwner = await fixture.Store.UpdateMemberRoleAsync(
            new UpdateOrganizationMemberRoleCommand(
                admin.UserId,
                organizationId,
                ownerMemberId,
                OrganizationRole.Member),
            TestContext.Current.CancellationToken);
        var ownerCannotEditSelf = await fixture.Store.UpdateMemberRoleAsync(
            new UpdateOrganizationMemberRoleCommand(
                owner.UserId,
                organizationId,
                ownerMemberId,
                OrganizationRole.Admin),
            TestContext.Current.CancellationToken);
        var unchanged = await fixture.Store.UpdateMemberRoleAsync(
            new UpdateOrganizationMemberRoleCommand(
                owner.UserId,
                organizationId,
                adminMemberId,
                OrganizationRole.Admin),
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.RoleAssignmentForbidden, hiddenTarget.Failure);
        Assert.Equal(
            OrganizationFailure.RoleAssignmentForbidden,
            adminCannotCreateOwner.Failure);
        Assert.Equal(
            OrganizationFailure.RoleAssignmentForbidden,
            adminCannotDemoteOwner.Failure);
        Assert.Equal(
            OrganizationFailure.RoleAssignmentForbidden,
            ownerCannotEditSelf.Failure);
        Assert.Equal(OrganizationFailure.MemberRoleUnchanged, unchanged.Failure);
    }

    [Fact]
    public async Task Set_active_updates_only_a_current_session_with_membership()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "active-owner@local-agent.test");
        var foreign = await fixture.CreateUserAndSessionAsync(
            "active-foreign@local-agent.test");
        var accessible = await fixture.SeedOrganizationForAsync(
            actor,
            "Accessible",
            "accessible",
            OrganizationRole.Owner);
        var inaccessible = await fixture.SeedOrganizationForAsync(
            foreign,
            "Inaccessible",
            "inaccessible",
            OrganizationRole.Owner);

        var denied = await fixture.Store.SetActiveAsync(
            new SetActiveOrganizationCommand(
                actor.UserId,
                actor.SessionId,
                inaccessible),
            TestContext.Current.CancellationToken);
        var selected = await fixture.Store.SetActiveAsync(
            new SetActiveOrganizationCommand(
                actor.UserId,
                actor.SessionId,
                accessible),
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.NotFound, denied.Failure);
        Assert.Equal(
            accessible,
            Assert.IsType<ActiveOrganization>(selected.Value).OrganizationId);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(
            accessible.Value,
            await db.Sessions
                .Where(row => row.Id == actor.SessionId.Value)
                .Select(row => row.ActiveOrganizationId)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Null(await db.Sessions
            .Where(row => row.Id == foreign.SessionId.Value)
            .Select(row => row.ActiveOrganizationId)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Browser_session_projects_active_organization_without_a_ticket_claim()
    {
        await using var fixture = await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "session-context@local-agent.test");
        var organizationId = await fixture.SeedOrganizationForAsync(
            actor,
            "Session Context",
            "session-context",
            OrganizationRole.Owner);
        await fixture.SetSessionActiveAsync(actor.SessionId, organizationId);

        var current = await fixture.GetCurrentBrowserSessionAsync(actor);

        Assert.Equal(
            organizationId,
            Assert.IsType<AuthenticatedSession>(current)
                .Session.ActiveOrganizationId);
    }

    private static OrganizationSlug OrganizationSlugForTest(string value)
    {
        Assert.True(OrganizationSlug.TryCreate(value, out var slug));
        return slug;
    }
}

internal sealed class OrganizationStoreFixture : IAsyncDisposable
{
    internal static readonly DateTimeOffset Now =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainerFixture _postgres;
    private readonly string _databaseName;
    private readonly string _connectionString;
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _storeScope;

    private OrganizationStoreFixture(
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

    internal IOrganizationStore Store =>
        _storeScope.ServiceProvider.GetRequiredService<IOrganizationStore>();

    internal string ConnectionString => _connectionString;

    internal static async Task<OrganizationStoreFixture> CreateAsync(
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
        services.AddSingleton<TimeProvider>(new FixedOrganizationTimeProvider(Now));
        services.AddSingleton<OrganizationNameConflictBarrier>();
        services.AddDbContext<TemplateDbContext>((provider, options) =>
            options.AddInterceptors(
                provider.GetRequiredService<OrganizationNameConflictBarrier>()));
        services.AddAuthInfrastructure(
            configuration,
            new TestHostEnvironment());
        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<TemplateDbContext>()
                .Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return new OrganizationStoreFixture(
            postgres,
            database.DatabaseName,
            database.ConnectionString,
            provider);
    }

    internal static async Task<OrganizationStoreFixture> CreateWithTwoOwnersAsync(
        PostgreSqlContainerFixture postgres)
    {
        var fixture = await CreateAsync(postgres);
        fixture.FirstOwner = await fixture.CreateUserAndSessionAsync(
            "race-first-owner@local-agent.test");
        fixture.SecondOwner = await fixture.CreateUserAndSessionAsync(
            "race-second-owner@local-agent.test");
        fixture.TwoOwnerOrganizationId = await fixture.SeedOrganizationForAsync(
            fixture.FirstOwner,
            "Owner Race",
            "owner-race",
            OrganizationRole.Owner);
        await fixture.AddMembershipAsync(
            fixture.TwoOwnerOrganizationId,
            fixture.SecondOwner,
            OrganizationRole.Owner);
        return fixture;
    }

    internal OrganizationActor FirstOwner { get; private set; } = default!;
    internal OrganizationActor SecondOwner { get; private set; } = default!;
    internal OrganizationId TwoOwnerOrganizationId { get; private set; }

    internal void CoordinateConcurrentNameChecks() =>
        _services
            .GetRequiredService<OrganizationNameConflictBarrier>()
            .CoordinateNextPair();

    internal TemplateDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        TemplateDbContext.Configure(options, _connectionString);
        return new TemplateDbContext(options.Options);
    }

    internal async Task<OrganizationActor> CreateUserAndSessionAsync(
        string email,
        string? displayName = null)
    {
        await using var db = CreateDbContext();
        var userId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
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
        db.Sessions.Add(new AuthSessionEntity
        {
            Id = sessionId,
            UserId = userId,
            TicketKeyHash = System.Security.Cryptography.SHA256.HashData(
                sessionId.ToByteArray()),
            ProtectedTicket = [1, 2, 3],
            CreatedAt = Now,
            UpdatedAt = Now,
            ExpiresAt = Now.AddDays(7),
            AuthenticationMethod = BrowserAuthenticationMethods.Local
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new OrganizationActor(
            new UserId(userId),
            new SessionId(sessionId));
    }

    internal async Task<OrganizationId> SeedOrganizationForAsync(
        OrganizationActor actor,
        string name,
        string slug,
        OrganizationRole role,
        OrganizationId? organizationId = null,
        OrganizationMemberId? memberId = null,
        DateTimeOffset? joinedAt = null)
    {
        var id = organizationId ?? OrganizationId.New();
        await using var db = CreateDbContext();
        db.Organizations.Add(new OrganizationEntity
        {
            Id = id.Value,
            Name = name,
            Slug = slug,
            CreatedAt = Now,
            UpdatedAt = Now
        });
        db.OrganizationMembers.Add(new OrganizationMemberEntity
        {
            Id = (memberId ?? OrganizationMemberId.New()).Value,
            OrganizationId = id.Value,
            UserId = actor.UserId.Value,
            Role = role.Value,
            JoinedAt = joinedAt ?? Now,
            UpdatedAt = joinedAt ?? Now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    internal async Task<OrganizationMemberId> AddMembershipAsync(
        OrganizationId organizationId,
        OrganizationActor actor,
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

    internal async Task SeedAllowedDomainsAsync(
        OrganizationId organizationId,
        params string[] domains)
    {
        await using var db = CreateDbContext();
        db.OrganizationAllowedEmailDomains.AddRange(domains.Select(domain =>
            new OrganizationAllowedEmailDomainEntity
            {
                OrganizationId = organizationId.Value,
                Domain = domain
            }));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal async Task SetSessionActiveAsync(
        SessionId sessionId,
        OrganizationId organizationId)
    {
        await using var db = CreateDbContext();
        await db.Sessions
            .Where(row => row.Id == sessionId.Value)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    row => row.ActiveOrganizationId,
                    organizationId.Value),
                TestContext.Current.CancellationToken);
    }

    internal async Task<int> CountMembershipsAsync(
        OrganizationId organizationId,
        UserId userId)
    {
        await using var db = CreateDbContext();
        return await db.OrganizationMembers.CountAsync(
            row =>
                row.OrganizationId == organizationId.Value &&
                row.UserId == userId.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<int> CountOwnersAsync()
    {
        await using var db = CreateDbContext();
        return await db.OrganizationMembers.CountAsync(
            row =>
                row.OrganizationId == TwoOwnerOrganizationId.Value &&
                row.Role == OrganizationRole.Owner.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<OrganizationMemberId> FindMemberIdAsync(
        OrganizationId organizationId,
        UserId userId)
    {
        await using var db = CreateDbContext();
        return new OrganizationMemberId(await db.OrganizationMembers
            .Where(row =>
                row.OrganizationId == organizationId.Value &&
                row.UserId == userId.Value)
            .Select(row => row.Id)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    internal async Task<OrganizationOperationResult<OrganizationMember>>
        DemoteOwnerAsync(OrganizationActor actor)
    {
        var target = actor == FirstOwner ? SecondOwner : FirstOwner;
        var targetMemberId = await FindMemberIdAsync(
            TwoOwnerOrganizationId,
            target.UserId);
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOrganizationStore>()
            .UpdateMemberRoleAsync(
                new UpdateOrganizationMemberRoleCommand(
                    actor.UserId,
                    TwoOwnerOrganizationId,
                    targetMemberId,
                    OrganizationRole.Member),
                TestContext.Current.CancellationToken);
    }

    internal async Task<OrganizationOperationResult<OrganizationDetail>>
        CreateOrganizationAsync(OrganizationActor actor, string name)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOrganizationStore>()
            .CreateAsync(
                new CreateOrganizationCommand(
                    actor.UserId,
                    actor.SessionId,
                    name),
                TestContext.Current.CancellationToken);
    }

    internal async Task<OrganizationOperationResult<OrganizationDetail>>
        UpdateOrganizationAsync(
            OrganizationActor actor,
            OrganizationId organizationId,
            string name)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOrganizationStore>()
            .UpdateAsync(
                new UpdateOrganizationCommand(
                    actor.UserId,
                    organizationId,
                    name,
                    Slug: null,
                    AllowedEmailDomains: null),
                TestContext.Current.CancellationToken);
    }

    internal async Task<OrganizationOperationResult<OrganizationDeletion>>
        DeleteOrganizationAsync(
            OrganizationActor actor,
            OrganizationId organizationId,
            string confirmationName)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOrganizationStore>()
            .DeleteAsync(
                new DeleteOrganizationCommand(
                    actor.UserId,
                    organizationId,
                    confirmationName),
                TestContext.Current.CancellationToken);
    }

    internal async Task<OrganizationOperationResult<OrganizationMember>>
        AddMemberAsync(
            OrganizationActor actor,
            OrganizationId organizationId,
            OrganizationActor target,
            OrganizationRole role)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOrganizationStore>()
            .AddMemberAsync(
                new AddOrganizationMemberCommand(
                    actor.UserId,
                    organizationId,
                    target.UserId,
                    role,
                    AcknowledgeDomainRestriction: true),
                TestContext.Current.CancellationToken);
    }

    internal async Task<int> CountAccessibleOrganizationsAsync(
        OrganizationActor actor)
    {
        await using var db = CreateDbContext();
        return await db.OrganizationMembers.CountAsync(
            row => row.UserId == actor.UserId.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<string> LowerInPostgresAsync(string value)
    {
        await using var db = CreateDbContext();
        return await db.Database
            .SqlQuery<string>(
                $"SELECT lower({value}) AS \"Value\"")
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<AuthenticatedSession?> GetCurrentBrowserSessionAsync(
        OrganizationActor actor)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(
                        BrowserSessionClaimTypes.SessionId,
                        actor.SessionId.Value.ToString())
                ],
                "test"))
        };
        scope.ServiceProvider
            .GetRequiredService<IHttpContextAccessor>()
            .HttpContext = context;
        return await scope.ServiceProvider
            .GetRequiredService<IBrowserSessionGateway>()
            .GetCurrentAsync(TestContext.Current.CancellationToken);
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

internal sealed record OrganizationActor(UserId UserId, SessionId SessionId);

internal sealed class FixedOrganizationTimeProvider(DateTimeOffset now)
    : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class OrganizationNameConflictBarrier : DbCommandInterceptor
{
    private int _enabled;
    private int _arrived;
    private TaskCompletionSource _release = NewSignal();

    internal void CoordinateNextPair()
    {
        _arrived = 0;
        _release = NewSignal();
        Volatile.Write(ref _enabled, 1);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _enabled) == 0 ||
            !IsNameConflictQuery(command))
        {
            return result;
        }

        if (Interlocked.Increment(ref _arrived) == 2)
        {
            Volatile.Write(ref _enabled, 0);
            _release.TrySetResult();
        }

        var timeout = Task.Delay(
            TimeSpan.FromSeconds(3),
            cancellationToken);
        await Task.WhenAny(_release.Task, timeout);
        Volatile.Write(ref _enabled, 0);
        _release.TrySetResult();
        return result;
    }

    private static bool IsNameConflictQuery(DbCommand command) =>
        command.CommandText.Contains(
            "SELECT EXISTS",
            StringComparison.OrdinalIgnoreCase) &&
        command.CommandText.Contains(
            "organizations.members",
            StringComparison.OrdinalIgnoreCase) &&
        command.CommandText.Contains(
            "lower(",
            StringComparison.OrdinalIgnoreCase);

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
