using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Collaboration;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Collaboration;

public sealed class CollaborationPersistenceModelTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private string _databaseName = string.Empty;
    private string _connectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        (_databaseName, _connectionString) = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Collaboration_entities_use_organizations_tables_and_uuid_keys()
    {
        using var db = CreateContext();

        AssertEntityStorage<TeamEntity>(db, "teams");
        AssertEntityStorage<TeamMemberEntity>(db, "team_members");
        AssertEntityStorage<InvitationEntity>(db, "invitations");
    }

    [Fact]
    public void Empty_invitation_ids_use_a_non_temporary_uuid_v4_fallback()
    {
        using var db = CreateContext();
        var invitation = new InvitationEntity
        {
            OrganizationId = Guid.CreateVersion7(),
            Email = "invitee@example.test",
            Role = "member",
            Status = "pending",
            InviterUserId = Guid.CreateVersion7(),
            ExpiresAt = Now.AddDays(2),
            CreatedAt = Now,
            UpdatedAt = Now
        };

        db.Invitations.Add(invitation);

        Assert.NotEqual(Guid.Empty, invitation.Id);
        Assert.Equal(4, invitation.Id.Version);
        Assert.False(db.Entry(invitation).Property(value => value.Id).IsTemporary);
    }

    [Fact]
    public void Teams_have_tenant_key_checks_required_timestamps_and_stable_list_index()
    {
        using var db = CreateContext();
        var entity = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(TeamEntity))!;

        Assert.Contains(
            entity.GetKeys(),
            key => key.Properties.Select(property => property.Name)
                .SequenceEqual(["OrganizationId", "Id"]));
        AssertRequiredTimestamp(entity, "CreatedAt");
        AssertRequiredTimestamp(entity, "UpdatedAt");
        Assert.Contains(
            entity.GetCheckConstraints(),
            check => check.Sql!.Contains("char_length(name)", StringComparison.Ordinal)
                     && check.Sql.Contains("50", StringComparison.Ordinal)
                     && check.Sql.Contains("btrim(name)", StringComparison.Ordinal)
                     && check.Sql.Contains("[:alnum:]", StringComparison.Ordinal));
        Assert.Contains(
            entity.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                         .SequenceEqual(["OrganizationId", "CreatedAt", "Id"]));

        var organizationForeignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(["OrganizationId"], PropertyNames(organizationForeignKey));
        Assert.Equal(DeleteBehavior.Cascade, organizationForeignKey.DeleteBehavior);
    }

    [Fact]
    public void Team_members_use_tenant_qualified_foreign_keys()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(TeamMemberEntity))!;
        var foreignKeys = entity.GetForeignKeys().ToArray();

        Assert.Contains(foreignKeys, foreignKey =>
            PropertyNames(foreignKey).SequenceEqual(["OrganizationId", "TeamId"])
            && PrincipalKeyNames(foreignKey).SequenceEqual(["OrganizationId", "Id"])
            && foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
        Assert.Contains(foreignKeys, foreignKey =>
            PropertyNames(foreignKey).SequenceEqual(
                ["OrganizationId", "OrganizationMemberId"])
            && PrincipalKeyNames(foreignKey).SequenceEqual(["OrganizationId", "Id"])
            && foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
        AssertRequiredTimestamp(entity, "JoinedAt");
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique
                     && index.Properties.Select(property => property.Name)
                         .SequenceEqual(["TeamId", "OrganizationMemberId"]));
        Assert.Contains(
            entity.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                         .SequenceEqual(["TeamId", "JoinedAt", "Id"]));
    }

    [Fact]
    public void Invitations_have_closed_checks_restrictive_team_reference_and_pending_uniqueness()
    {
        using var db = CreateContext();
        var entity = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(InvitationEntity))!;

        AssertRequiredTimestamp(entity, "ExpiresAt");
        AssertRequiredTimestamp(entity, "CreatedAt");
        AssertRequiredTimestamp(entity, "UpdatedAt");
        Assert.Contains(
            entity.GetCheckConstraints(),
            check => ContainsAll(check.Sql, "role", "owner", "admin", "member"));
        Assert.Contains(
            entity.GetCheckConstraints(),
            check => ContainsAll(
                check.Sql,
                "status",
                "pending",
                "accepted",
                "rejected",
                "canceled"));
        Assert.Contains(
            entity.GetCheckConstraints(),
            check => ContainsAll(check.Sql, "email", "lower(email)", "254"));
        Assert.Contains(
            entity.GetCheckConstraints(),
            check => ContainsAll(check.Sql, "expires_at", "created_at", ">"));

        var teamForeignKey = Assert.Single(
            entity.GetForeignKeys(),
            foreignKey => PropertyNames(foreignKey)
                .SequenceEqual(["OrganizationId", "TeamId"]));
        Assert.Equal(["OrganizationId", "Id"], PrincipalKeyNames(teamForeignKey));
        Assert.Equal(DeleteBehavior.Restrict, teamForeignKey.DeleteBehavior);

        var organizationForeignKey = Assert.Single(
            entity.GetForeignKeys(),
            foreignKey => PropertyNames(foreignKey).SequenceEqual(["OrganizationId"]));
        Assert.Equal(DeleteBehavior.Cascade, organizationForeignKey.DeleteBehavior);
        var inviterForeignKey = Assert.Single(
            entity.GetForeignKeys(),
            foreignKey => PropertyNames(foreignKey).SequenceEqual(["InviterUserId"]));
        Assert.Equal(DeleteBehavior.Cascade, inviterForeignKey.DeleteBehavior);

        var pendingIndex = Assert.Single(
            entity.GetIndexes(),
            index => index.IsUnique
                     && index.Properties.Select(property => property.Name)
                         .SequenceEqual(["OrganizationId", "Email"]));
        Assert.Equal("status = 'pending'", pendingIndex.GetFilter());
    }

    [Fact]
    public void Sessions_do_not_persist_an_active_team_preference()
    {
        using var db = CreateContext();
        var session = db.Model.FindEntityType(typeof(AuthSessionEntity))!;

        Assert.Null(session.FindProperty("ActiveTeamId"));
        Assert.Null(typeof(AuthSessionEntity).GetProperty("ActiveTeamId"));
    }

    [Fact]
    public void Collaboration_indexes_use_PostgreSql_safe_identifier_lengths()
    {
        using var db = CreateContext();
        var collaborationTypes = new[]
        {
            typeof(TeamEntity),
            typeof(TeamMemberEntity),
            typeof(InvitationEntity)
        };

        var names = collaborationTypes
            .Select(type => db.Model.FindEntityType(type)!)
            .SelectMany(entity => entity.GetIndexes())
            .Select(index => index.GetDatabaseName())
            .Where(name => name is not null)
            .ToArray();

        Assert.All(names, name => Assert.InRange(name!.Length, 1, 63));
    }

    [Fact]
    public async Task Migration_creates_expression_partial_and_cursor_indexes()
    {
        Assert.Equal(
            ["allowed_email_domains", "invitations", "members", "organizations", "team_members", "teams"],
            await ReadTablesAsync());
        Assert.Contains(
            "UNIQUE INDEX ux_teams_organization_id_lower_name",
            await ReadIndexDefinitionAsync("ux_teams_organization_id_lower_name"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "(organization_id, lower((name)::text))",
            await ReadIndexDefinitionAsync("ux_teams_organization_id_lower_name"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "WHERE ((status)::text = 'pending'::text)",
            await ReadIndexDefinitionAsync("ux_invitations_organization_id_email_pending"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "RESTRICT",
            await ReadDeleteRuleAsync("invitations", "team_id"));
        Assert.Equal(
            "CASCADE",
            await ReadDeleteRuleAsync("team_members", "organization_member_id"));
    }

    [Fact]
    public async Task Organization_and_identity_cleanup_remain_cascading()
    {
        await using var db = CreateContext();
        var organizationId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var teamMemberId = Guid.CreateVersion7();
        var userId = await InsertUserAsync(db, "collaboration-cleanup@example.test");

        await InsertCollaborationGraphAsync(
            db,
            organizationId,
            teamId,
            memberId,
            teamMemberId,
            userId,
            invitationTeamId: teamId);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM auth.users WHERE id = {userId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(0L, await CountAsync("members", "id", memberId));
        Assert.Equal(0L, await CountAsync("team_members", "id", teamMemberId));
        Assert.Equal(0L, await CountAsync("invitations", "inviter_user_id", userId));
        Assert.Equal(1L, await CountAsync("teams", "id", teamId));

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM organizations.organizations WHERE id = {organizationId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(0L, await CountAsync("teams", "id", teamId));
    }

    [Fact]
    public async Task Deleting_an_organization_cascades_its_complete_collaboration_graph()
    {
        await using var db = CreateContext();
        var organizationId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var teamMemberId = Guid.CreateVersion7();
        var userId = await InsertUserAsync(db, "organization-delete@example.test");
        var invitationId = await InsertCollaborationGraphAsync(
            db,
            organizationId,
            teamId,
            memberId,
            teamMemberId,
            userId,
            invitationTeamId: teamId);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM organizations.organizations WHERE id = {organizationId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(0L, await CountAsync("teams", "id", teamId));
        Assert.Equal(0L, await CountAsync("team_members", "id", teamMemberId));
        Assert.Equal(0L, await CountAsync("invitations", "id", invitationId));
        Assert.Equal(1L, await CountAuthUsersAsync(userId));
    }

    [Fact]
    public async Task Team_deletion_requires_detaching_invitation_history()
    {
        await using var db = CreateContext();
        var organizationId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var teamMemberId = Guid.CreateVersion7();
        var userId = await InsertUserAsync(db, "team-delete@example.test");
        var invitationId = await InsertCollaborationGraphAsync(
            db,
            organizationId,
            teamId,
            memberId,
            teamMemberId,
            userId,
            invitationTeamId: teamId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM organizations.teams WHERE id = {teamId}",
                TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE organizations.invitations SET team_id = NULL WHERE id = {invitationId}",
            TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM organizations.teams WHERE id = {teamId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(0L, await CountAsync("teams", "id", teamId));
        Assert.Equal(1L, await CountAsync("invitations", "id", invitationId));
    }

    private static void AssertEntityStorage<TEntity>(TemplateDbContext db, string tableName)
    {
        var entity = db.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entity);
        Assert.Equal(tableName, entity.GetTableName());
        Assert.Equal("organizations", entity.GetSchema());
        var key = Assert.Single(entity.FindPrimaryKey()!.Properties);
        Assert.Equal("Id", key.Name);
        Assert.Equal(typeof(Guid), key.ClrType);
    }

    private static void AssertRequiredTimestamp(IEntityType entity, string propertyName)
    {
        var property = entity.FindProperty(propertyName);
        Assert.NotNull(property);
        Assert.False(property.IsNullable);
        Assert.Equal(typeof(DateTimeOffset), property.ClrType);
        Assert.Equal("timestamp with time zone", property.GetColumnType());
    }

    private static string[] PropertyNames(IForeignKey foreignKey) =>
        foreignKey.Properties.Select(property => property.Name).ToArray();

    private static string[] PrincipalKeyNames(IForeignKey foreignKey) =>
        foreignKey.PrincipalKey.Properties.Select(property => property.Name).ToArray();

    private static bool ContainsAll(string? value, params string[] fragments) =>
        value is not null
        && fragments.All(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<string>> ReadTablesAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'organizations'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """;
        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        var tables = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private async Task<string> ReadIndexDefinitionAsync(string indexName)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'organizations'
              AND indexname = @indexName
            """;
        command.Parameters.AddWithValue("indexName", indexName);
        return (string)(await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken))!;
    }

    private async Task<string?> ReadDeleteRuleAsync(string table, string column)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT referential.delete_rule
            FROM information_schema.referential_constraints AS referential
            INNER JOIN information_schema.key_column_usage AS columns
                ON columns.constraint_catalog = referential.constraint_catalog
                AND columns.constraint_schema = referential.constraint_schema
                AND columns.constraint_name = referential.constraint_name
            WHERE columns.table_schema = 'organizations'
              AND columns.table_name = @table
              AND columns.column_name = @column
            """;
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        return (string?)await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> InsertUserAsync(TemplateDbContext db, string email)
    {
        var id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO auth.users (
                id,
                user_name,
                normalized_user_name,
                email,
                normalized_email,
                email_confirmed,
                password_hash,
                security_stamp,
                concurrency_stamp,
                phone_number,
                phone_number_confirmed,
                two_factor_enabled,
                lockout_end,
                lockout_enabled,
                access_failed_count,
                display_name,
                image_url,
                is_local_automation,
                created_at,
                updated_at)
            VALUES (
                {id},
                {email},
                {email.ToUpperInvariant()},
                {email},
                {email.ToUpperInvariant()},
                {true},
                {null},
                {Guid.NewGuid().ToString("N")},
                {Guid.NewGuid().ToString("N")},
                {null},
                {false},
                {false},
                {null},
                {false},
                {0},
                {"Collaboration User"},
                {null},
                {true},
                {Now},
                {Now})
            """,
            TestContext.Current.CancellationToken);
        return id;
    }

    private static async Task<Guid> InsertCollaborationGraphAsync(
        TemplateDbContext db,
        Guid organizationId,
        Guid teamId,
        Guid memberId,
        Guid teamMemberId,
        Guid userId,
        Guid? invitationTeamId)
    {
        var invitationId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO organizations.organizations (
                id, name, slug, created_at, updated_at)
            VALUES (
                {organizationId}, {"Cleanup Organization"},
                {"cleanup-" + organizationId.ToString("N")}, {Now}, {Now});

            INSERT INTO organizations.members (
                id, organization_id, user_id, role, joined_at, updated_at)
            VALUES (
                {memberId}, {organizationId}, {userId}, {"owner"}, {Now}, {Now});

            INSERT INTO organizations.teams (
                id, organization_id, name, created_at, updated_at)
            VALUES (
                {teamId}, {organizationId}, {"Design"}, {Now}, {Now});

            INSERT INTO organizations.team_members (
                id, organization_id, team_id, organization_member_id, joined_at)
            VALUES (
                {teamMemberId}, {organizationId}, {teamId}, {memberId}, {Now});

            INSERT INTO organizations.invitations (
                id,
                organization_id,
                team_id,
                email,
                role,
                status,
                inviter_user_id,
                expires_at,
                created_at,
                updated_at)
            VALUES (
                {invitationId},
                {organizationId},
                {invitationTeamId},
                {"invitee@example.test"},
                {"member"},
                {"pending"},
                {userId},
                {Now.AddDays(2)},
                {Now},
                {Now})
            """,
            TestContext.Current.CancellationToken);
        return invitationId;
    }

    private async Task<long> CountAsync(string table, string column, Guid value)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM organizations.{table} WHERE {column} = @value";
        command.Parameters.AddWithValue("value", value);
        return (long)(await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken))!;
    }

    private async Task<long> CountAuthUsersAsync(Guid userId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM auth.users WHERE id = @userId";
        command.Parameters.AddWithValue("userId", userId);
        return (long)(await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken))!;
    }

    private TemplateDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        TemplateDbContext.Configure(options, _connectionString);
        return new TemplateDbContext(options.Options);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_databaseName.Length > 0)
        {
            await postgres.DropDatabaseAsync(
                _databaseName,
                TestContext.Current.CancellationToken);
        }
    }
}
