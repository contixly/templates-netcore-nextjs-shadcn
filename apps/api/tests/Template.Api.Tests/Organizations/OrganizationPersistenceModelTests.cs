using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Api.Tests.Infrastructure;
using Template.Application.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Organizations;

public sealed class OrganizationPersistenceModelTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

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
    public async Task MigrationCreatesOrganizationSchemaConstraintsAndIndexes()
    {
        Assert.Equal(
            [
                "allowed_email_domains",
                "invitations",
                "members",
                "organizations",
                "team_members",
                "teams"
            ],
            await ReadTablesAsync("organizations"));
        Assert.Equal(
            "SET NULL",
            await ReadDeleteRuleAsync("auth", "sessions", "active_organization_id"));
        Assert.True(await HasIndexAsync(
            "organizations",
            "members",
            isUnique: true,
            "organization_id",
            "user_id"));
        Assert.True(await HasIndexAsync(
            "organizations",
            "members",
            isUnique: false,
            "user_id",
            "joined_at",
            "id"));
        Assert.True(await HasCheckContainingAsync(
            "organizations",
            "members",
            "role",
            "owner",
            "admin",
            "member"));
    }

    [Fact]
    public async Task DeletingOrganizationClearsActiveSessionPreference()
    {
        await using var db = CreateContext();
        var user = CreateUser("active-organization@example.test");
        var session = CreateSession(user.Id, 0x41);
        db.Users.Add(user);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var organizationId = Guid.CreateVersion7();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO organizations.organizations (
                id,
                name,
                slug,
                created_at,
                updated_at)
            VALUES (
                {organizationId},
                {"Active Organization"},
                {"active-organization"},
                {Now},
                {Now})
            """,
            TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE auth.sessions
            SET active_organization_id = {organizationId}
            WHERE id = {session.Id}
            """,
            TestContext.Current.CancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM organizations.organizations WHERE id = {organizationId}",
            TestContext.Current.CancellationToken);

        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT active_organization_id
            FROM auth.sessions
            WHERE id = @sessionId
            """;
        command.Parameters.AddWithValue("sessionId", session.Id);
        Assert.Equal(
            DBNull.Value,
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletingIdentityUserCascadesOrganizationMembership()
    {
        await using var db = CreateContext();
        var user = CreateUser("organization-member@example.test");
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var organizationId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO organizations.organizations (
                id,
                name,
                slug,
                created_at,
                updated_at)
            VALUES (
                {organizationId},
                {"Membership Organization"},
                {"membership-organization"},
                {Now},
                {Now});

            INSERT INTO organizations.members (
                id,
                organization_id,
                user_id,
                role,
                joined_at,
                updated_at)
            VALUES (
                {memberId},
                {organizationId},
                {user.Id},
                {"member"},
                {Now},
                {Now});
            """,
            TestContext.Current.CancellationToken);

        db.Users.Remove(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT count(*)
            FROM organizations.members
            WHERE id = @memberId
            """;
        command.Parameters.AddWithValue("memberId", memberId);
        Assert.Equal(
            0L,
            (long)(await command.ExecuteScalarAsync(
                TestContext.Current.CancellationToken))!);
    }

    private async Task<IReadOnlyList<string>> ReadTablesAsync(string schema)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = @schema
              AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """;
        command.Parameters.AddWithValue("schema", schema);

        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        var tables = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private async Task<string?> ReadDeleteRuleAsync(
        string schema,
        string table,
        string column)
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
            WHERE columns.table_schema = @schema
              AND columns.table_name = @table
              AND columns.column_name = @column
            """;
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        return (string?)await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken);
    }

    private async Task<bool> HasIndexAsync(
        string schema,
        string table,
        bool isUnique,
        params string[] columns)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_index AS index
                INNER JOIN pg_class AS relation ON relation.oid = index.indrelid
                INNER JOIN pg_namespace AS namespace
                    ON namespace.oid = relation.relnamespace
                WHERE namespace.nspname = @schema
                  AND relation.relname = @table
                  AND index.indisunique = @isUnique
                  AND (
                      SELECT array_agg(
                          attribute.attname::text
                          ORDER BY key.ordinality)
                      FROM unnest(index.indkey) WITH ORDINALITY
                          AS key(attribute_number, ordinality)
                      INNER JOIN pg_attribute AS attribute
                          ON attribute.attrelid = relation.oid
                          AND attribute.attnum = key.attribute_number
                  ) = @columns)
            """;
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("isUnique", isUnique);
        command.Parameters.AddWithValue("columns", columns);
        return (bool)(await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken))!;
    }

    private async Task<bool> HasCheckContainingAsync(
        string schema,
        string table,
        params string[] fragments)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT pg_get_constraintdef(con.oid)
            FROM pg_constraint AS con
            INNER JOIN pg_class AS relation ON relation.oid = con.conrelid
            INNER JOIN pg_namespace AS namespace
                ON namespace.oid = relation.relnamespace
            WHERE namespace.nspname = @schema
              AND relation.relname = @table
              AND con.contype = 'c'
            """;
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);

        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            var definition = reader.GetString(0);
            if (fragments.All(fragment => definition.Contains(
                    fragment,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
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

    private static ApplicationUser CreateUser(string email) => new()
    {
        Id = Guid.CreateVersion7(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        DisplayName = "Organization User",
        IsLocalAutomation = true,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static AuthSessionEntity CreateSession(Guid userId, byte hashByte) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        TicketKeyHash = Enumerable.Repeat(hashByte, 32).ToArray(),
        ProtectedTicket = [1, 2, 3],
        CreatedAt = Now,
        UpdatedAt = Now,
        ExpiresAt = Now.AddDays(7),
        AuthenticationMethod = BrowserAuthenticationMethods.Local
    };

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
