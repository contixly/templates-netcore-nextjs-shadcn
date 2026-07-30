using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using Template.Application.Accounts;
using Template.Application.Accounts.Ports;
using Template.Application.Authentication;
using Template.Api.Tests.Infrastructure;
using Template.Domain.Accounts;
using Template.Domain.Authentication;
using Template.Infrastructure.Accounts;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Accounts;

public sealed class AccountPersistenceTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 9, 30, 0, TimeSpan.Zero);
    private static readonly ExternalProvider[] ConfiguredProviders =
    [
        ExternalProvider.Google,
        ExternalProvider.GitHub,
        ExternalProvider.GitLab,
        ExternalProvider.Vk,
        ExternalProvider.Yandex
    ];

    private string _databaseName = string.Empty;
    private string _connectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        (_databaseName, _connectionString) = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MigrationBackfillsOnePrimaryEmailPerExistingUser()
    {
        var (databaseName, connectionString) = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        try
        {
            await using var db = CreateContext(connectionString);
            await db.Database.MigrateAsync(
                "20260724142511_InitialAuthPersistence",
                TestContext.Current.CancellationToken);
            var user = CreateUser("owner@example.test");
            user.EmailConfirmed = true;
            db.Users.Add(user);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

            var email = await db.UserEmails.SingleAsync(
                TestContext.Current.CancellationToken);
            Assert.Equal(user.Id, email.UserId);
            Assert.True(email.IsPrimary);
            Assert.Equal("owner@example.test", email.Email);
            Assert.Equal("OWNER@EXAMPLE.TEST", email.NormalizedEmail);
            Assert.Equal(user.CreatedAt, email.CreatedAt);
        }
        finally
        {
            await postgres.DropDatabaseAsync(
                databaseName,
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SessionAuthenticationMethodMigrationBackfillsExistingSessionAsLocal()
    {
        var (databaseName, connectionString) = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        try
        {
            await using var db = CreateContext(connectionString);
            await db.Database.MigrateAsync(
                "20260728232503_AccountsExternalOAuth",
                TestContext.Current.CancellationToken);
            var user = CreateUser("legacy-session@example.test");
            db.Users.Add(user);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var sessionId = Guid.CreateVersion7();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO auth.sessions (
                    id,
                    user_id,
                    ticket_key_hash,
                    protected_ticket,
                    created_at,
                    updated_at,
                    expires_at)
                VALUES (
                    {sessionId},
                    {user.Id},
                    {Enumerable.Repeat((byte)0x4A, 32).ToArray()},
                    {new byte[] { 0xCA, 0xFE }},
                    {Now},
                    {Now},
                    {Now.AddDays(7)})
                """,
                TestContext.Current.CancellationToken);

            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
            db.ChangeTracker.Clear();

            var session = await db.Sessions.AsNoTracking().SingleAsync(
                row => row.Id == sessionId,
                TestContext.Current.CancellationToken);
            Assert.Equal(BrowserAuthenticationMethods.Local, session.AuthenticationMethod);
        }
        finally
        {
            await postgres.DropDatabaseAsync(
                databaseName,
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DatabaseRejectsUnsupportedSessionAuthenticationMethod()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var user = CreateUser("invalid-session-method@example.test");
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var session = CreateSession(user.Id, Now, 0x5A);
        session.AuthenticationMethod = "saml";
        db.Sessions.Add(session);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task MigrationAddsRequiredConstraintsStateTablesAndNoProviderTokens()
    {
        await MigrateAsync();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var tables = await QueryStringsAsync(
            connection,
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'auth'
            ORDER BY table_name
            """);
        Assert.Contains("user_emails", tables);
        Assert.Contains("data_protection_keys", tables);
        Assert.Contains("openiddict_applications", tables);
        Assert.Contains("openiddict_authorizations", tables);
        Assert.Contains("openiddict_scopes", tables);
        Assert.Contains("openiddict_tokens", tables);

        var partialPrimaryIndexes = await QueryStringsAsync(
            connection,
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'auth'
              AND tablename = 'user_emails'
              AND indexdef ILIKE '%UNIQUE%'
              AND indexdef ILIKE '%WHERE%is_primary%'
            """);
        Assert.Single(partialPrimaryIndexes);

        var loginColumns = await QueryStringsAsync(
            connection,
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'auth' AND table_name = 'user_logins'
            ORDER BY column_name
            """);
        Assert.Equal(
            [
                "connected_at",
                "last_used_at",
                "login_provider",
                "provider_display_name",
                "provider_key",
                "user_id",
                "verified_email_id"
            ],
            loginColumns);
        Assert.DoesNotContain(loginColumns, value =>
            value.Contains("token", StringComparison.OrdinalIgnoreCase));

        var cascadeForeignKeys = await QueryStringsAsync(
            connection,
            """
            SELECT child.relname || '->' || parent.relname
            FROM pg_constraint AS con
            INNER JOIN pg_class AS child ON child.oid = con.conrelid
            INNER JOIN pg_class AS parent ON parent.oid = con.confrelid
            WHERE con.connamespace = 'auth'::regnamespace
              AND con.contype = 'f'
              AND con.confdeltype = 'c'
              AND (
                  (child.relname = 'user_emails' AND parent.relname = 'users')
                  OR (child.relname = 'user_logins' AND parent.relname IN ('users', 'user_emails'))
              )
            ORDER BY child.relname, parent.relname
            """);
        Assert.Equal(3, cascadeForeignKeys.Count);
    }

    [Fact]
    public async Task DatabaseEnforcesGlobalEmailOnePrimaryAndOneProviderPerUser()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var first = CreateUser("first@example.test");
        var second = CreateUser("second@example.test");
        db.Users.AddRange(first, second);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var primary = AddEmail(db, first, "first@example.test", primary: true);
        AddEmail(db, second, "shared@example.test", primary: true);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        AddEmail(db, first, "shared@example.test", primary: false);
        await AssertUniqueViolationAsync(db);
        db.ChangeTracker.Clear();

        db.UserEmails.Add(new UserEmailEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = first.Id,
            Email = "other@example.test",
            NormalizedEmail = "OTHER@EXAMPLE.TEST",
            IsPrimary = true,
            CreatedAt = Now
        });
        await AssertUniqueViolationAsync(db);
        db.ChangeTracker.Clear();

        db.UserLogins.AddRange(
            CreateLogin(first.Id, primary.Id, ExternalProvider.Google, "google-1"),
            CreateLogin(first.Id, primary.Id, ExternalProvider.Google, "google-2"));
        await AssertUniqueViolationAsync(db);
    }

    [Fact]
    public async Task UniqueEmailRaceIsClassifiedAsAccountConcurrencyException()
    {
        await MigrateAsync();
        Guid firstId;
        Guid secondId;
        await using (var seed = CreateContext())
        {
            var first = CreateUser("race-one@example.test");
            var second = CreateUser("race-two@example.test");
            firstId = first.Id;
            secondId = second.Id;
            seed.Users.AddRange(first, second);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var firstDb = CreateContext();
        await using var secondDb = CreateContext();
        await firstDb.Database.OpenConnectionAsync(
            TestContext.Current.CancellationToken);
        await secondDb.Database.OpenConnectionAsync(
            TestContext.Current.CancellationToken);
        var blockingPid = ((NpgsqlConnection)firstDb.Database.GetDbConnection()).ProcessID;
        var blockedPid = ((NpgsqlConnection)secondDb.Database.GetDbConnection()).ProcessID;
        Assert.NotEqual(blockingPid, blockedPid);
        var firstStore = new EfExternalAccountStore(firstDb, TimeProvider.System);
        var secondStore = new EfExternalAccountStore(secondDb, TimeProvider.System);
        var email = VerifiedEmail.Create("race-shared@example.test");

        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        await firstStore.EnsureVerifiedEmailAsync(
            new UserId(firstId),
            email,
            primary: false,
            TestContext.Current.CancellationToken);

        var competingLink = Task.Run(
            () => secondStore.EnsureVerifiedEmailAsync(
                new UserId(secondId),
                email,
                primary: false,
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        Assert.True(await WaitUntilBlockedAsync(
            blockedPid,
            blockingPid,
            TimeSpan.FromSeconds(5)));

        await firstTransaction.CommitAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AccountConcurrencyException>(() => competingLink);

        await using var verify = CreateContext();
        var ownership = await verify.UserEmails.AsNoTracking()
            .Where(row => row.NormalizedEmail == email.NormalizedValue)
            .Select(row => row.UserId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal([firstId], ownership);
        Assert.False(await verify.UserEmails.AnyAsync(
            row => row.UserId == secondId
                && row.NormalizedEmail == email.NormalizedValue,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExternalStorePersistsAndRefreshesVerifiedLoginMetadata()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var store = new EfExternalAccountStore(db, new FixedTimeProvider(Now));
        var transactions = new EfApplicationUnitOfWork(db);
        var primaryIdentity = Identity(
            ExternalProvider.Google,
            "google-subject",
            "owner@example.test",
            "Provider Owner",
            "https://cdn.example.test/avatar.png");

        var created = await transactions.ExecuteAsync(
            async ct =>
            {
                var user = await store.CreateUserAsync(primaryIdentity, ct);
                await store.EnsureVerifiedEmailAsync(
                    user.Id,
                    primaryIdentity.Email,
                    primary: true,
                    ct);
                await store.AddLoginAsync(
                    user.Id,
                    primaryIdentity,
                    Now.AddMinutes(1),
                    usedForSignIn: false,
                    ct);
                return user;
            },
            TestContext.Current.CancellationToken);

        var (foundByEmail, login) = await transactions.ExecuteAsync(
            async ct => (
                await store.FindUserByEmailAsync(
                    primaryIdentity.Email.NormalizedValue,
                    ct),
                await store.FindLoginAsync(
                    ExternalProvider.Google,
                    "google-subject",
                    ct)),
            TestContext.Current.CancellationToken);
        Assert.Equal(created.Id, foundByEmail?.Id);
        Assert.Equal(Now.AddMinutes(1), login?.ConnectedAt);
        Assert.Null(login?.LastUsedAt);
        Assert.True(await store.IsEmailVouchedAsync(
            created.Id,
            primaryIdentity.Email.NormalizedValue,
            TestContext.Current.CancellationToken));

        var refreshedIdentity = Identity(
            ExternalProvider.Google,
            "google-subject",
            "new-owner@example.test",
            "Updated Owner",
            "https://cdn.example.test/new-avatar.png");
        await transactions.ExecuteAsync(
            async ct =>
            {
                await store.EnsureVerifiedEmailAsync(
                    created.Id,
                    refreshedIdentity.Email,
                    primary: false,
                    ct);
                await store.UpdateLoginEmailAsync(
                    created.Id,
                    refreshedIdentity,
                    usedAt: null,
                    ct);
                await store.UpdateLinkedProfileAsync(
                    created.Id,
                    refreshedIdentity.DisplayName,
                    refreshedIdentity.ImageUrl,
                    ct);
                return true;
            },
            TestContext.Current.CancellationToken);

        login = await transactions.ExecuteAsync(
            ct => store.FindLoginAsync(
                ExternalProvider.Google,
                "google-subject",
                ct),
            TestContext.Current.CancellationToken);
        Assert.Equal("NEW-OWNER@EXAMPLE.TEST", login?.Email.NormalizedValue);
        Assert.Null(login?.LastUsedAt);
        Assert.False(await store.IsEmailVouchedAsync(
            created.Id,
            primaryIdentity.Email.NormalizedValue,
            TestContext.Current.CancellationToken));
        Assert.True(await store.IsEmailVouchedAsync(
            created.Id,
            refreshedIdentity.Email.NormalizedValue,
            TestContext.Current.CancellationToken));
        Assert.False(await db.UserEmails.AnyAsync(
            row => row.NormalizedEmail == "OWNER@EXAMPLE.TEST" && !row.IsPrimary,
            TestContext.Current.CancellationToken));

        await transactions.ExecuteAsync(
            async ct =>
            {
                await store.UpdateLoginEmailAsync(
                    created.Id,
                    refreshedIdentity,
                    Now.AddMinutes(5),
                    ct);
                return true;
            },
            TestContext.Current.CancellationToken);
        login = await transactions.ExecuteAsync(
            ct => store.FindLoginAsync(
                ExternalProvider.Google,
                "google-subject",
                ct),
            TestContext.Current.CancellationToken);
        var user = await db.Users.SingleAsync(
            row => row.Id == created.Id.Value,
            TestContext.Current.CancellationToken);
        Assert.Equal(Now.AddMinutes(5), login?.LastUsedAt);
        Assert.Equal("Updated Owner", user.DisplayName);
        Assert.Equal("https://cdn.example.test/new-avatar.png", user.ImageUrl);
        Assert.Equal("owner@example.test", user.Email);
    }

    [Fact]
    public async Task ConcurrentExistingLoginEmailChangesSerializeAndLeaveNoOrphan()
    {
        await MigrateAsync();
        Guid userId;
        Guid primaryEmailId;
        Guid sharedEmailId;
        await using (var seed = CreateContext())
        {
            var user = CreateUser("race-primary@example.test");
            seed.Users.Add(user);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            var primary = AddEmail(
                seed,
                user,
                "race-primary@example.test",
                primary: true);
            var old = AddEmail(
                seed,
                user,
                "race-old-a@example.test",
                primary: false);
            var shared = AddEmail(
                seed,
                user,
                "race-shared@example.test",
                primary: false);
            seed.UserLogins.AddRange(
                CreateLogin(
                    user.Id,
                    old.Id,
                    ExternalProvider.Google,
                    "google-racing-email"),
                CreateLogin(
                    user.Id,
                    shared.Id,
                    ExternalProvider.GitHub,
                    "github-shared-email"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            userId = user.Id;
            primaryEmailId = primary.Id;
            sharedEmailId = shared.Id;
        }

        await using var firstDb = CreateContext();
        await using var secondDb = CreateContext();
        await firstDb.Database.OpenConnectionAsync(
            TestContext.Current.CancellationToken);
        await secondDb.Database.OpenConnectionAsync(
            TestContext.Current.CancellationToken);
        var firstPid =
            ((NpgsqlConnection)firstDb.Database.GetDbConnection()).ProcessID;
        var secondPid =
            ((NpgsqlConnection)secondDb.Database.GetDbConnection()).ProcessID;
        Assert.NotEqual(firstPid, secondPid);
        var firstStore = new EfExternalAccountStore(firstDb, TimeProvider.System);
        var secondStore = new EfExternalAccountStore(secondDb, TimeProvider.System);
        var firstIncoming = Identity(
            ExternalProvider.Google,
            "google-racing-email",
            "race-incoming-b@example.test");
        var secondIncoming = Identity(
            ExternalProvider.Google,
            "google-racing-email",
            "race-incoming-c@example.test");

        await using var firstTransaction =
            await firstDb.Database.BeginTransactionAsync(
                TestContext.Current.CancellationToken);
        await firstStore.EnsureVerifiedEmailAsync(
            new UserId(userId),
            firstIncoming.Email,
            primary: false,
            TestContext.Current.CancellationToken);
        await firstStore.UpdateLoginEmailAsync(
            new UserId(userId),
            firstIncoming,
            usedAt: null,
            TestContext.Current.CancellationToken);

        await using var secondTransaction =
            await secondDb.Database.BeginTransactionAsync(
                TestContext.Current.CancellationToken);
        await secondStore.EnsureVerifiedEmailAsync(
            new UserId(userId),
            secondIncoming.Email,
            primary: false,
            TestContext.Current.CancellationToken);
        var competingUpdate = Task.Run(
            () => secondStore.UpdateLoginEmailAsync(
                new UserId(userId),
                secondIncoming,
                usedAt: null,
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        Assert.True(await WaitUntilBlockedAsync(
            secondPid,
            firstPid,
            TimeSpan.FromSeconds(5)));
        Assert.False(competingUpdate.IsCompleted);

        await firstTransaction.CommitAsync(TestContext.Current.CancellationToken);
        await competingUpdate;
        await secondTransaction.CommitAsync(TestContext.Current.CancellationToken);

        await using var verify = CreateContext();
        var finalEmail = await (
            from login in verify.UserLogins.AsNoTracking()
            join email in verify.UserEmails.AsNoTracking()
                on login.VerifiedEmailId equals email.Id
            where login.UserId == userId
                && login.LoginProvider == ExternalProvider.Google.Value
                && login.ProviderKey == "google-racing-email"
            select email.NormalizedEmail).SingleAsync(
                TestContext.Current.CancellationToken);
        Assert.Contains(
            finalEmail,
            new[]
            {
                firstIncoming.Email.NormalizedValue,
                secondIncoming.Email.NormalizedValue
            });
        Assert.Equal(1, await verify.UserEmails.CountAsync(
            row => row.UserId == userId
                && (row.NormalizedEmail ==
                    firstIncoming.Email.NormalizedValue
                    || row.NormalizedEmail ==
                    secondIncoming.Email.NormalizedValue),
            TestContext.Current.CancellationToken));
        Assert.False(await verify.UserEmails.AnyAsync(
            row => row.UserId == userId
                && row.NormalizedEmail == "RACE-OLD-A@EXAMPLE.TEST",
            TestContext.Current.CancellationToken));
        Assert.True(await verify.UserEmails.AnyAsync(
            row => row.Id == primaryEmailId && row.IsPrimary,
            TestContext.Current.CancellationToken));
        Assert.True(await verify.UserEmails.AnyAsync(
            row => row.Id == sharedEmailId && !row.IsPrimary,
            TestContext.Current.CancellationToken));
        Assert.True(await verify.UserLogins.AnyAsync(
            row => row.UserId == userId
                && row.LoginProvider == ExternalProvider.GitHub.Value
                && row.VerifiedEmailId == sharedEmailId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentReconciliationLocksFromExistingLoginReadAndBothCallbacksSucceed()
    {
        await MigrateAsync();
        Guid userId;
        Guid primaryEmailId;
        Guid sharedEmailId;
        await using (var seed = CreateContext())
        {
            var user = CreateUser("reconcile-race-primary@example.test");
            seed.Users.Add(user);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            var primary = AddEmail(
                seed,
                user,
                "reconcile-race-primary@example.test",
                primary: true);
            var old = AddEmail(
                seed,
                user,
                "reconcile-race-old-a@example.test",
                primary: false);
            var shared = AddEmail(
                seed,
                user,
                "reconcile-race-shared@example.test",
                primary: false);
            seed.UserLogins.AddRange(
                CreateLogin(
                    user.Id,
                    old.Id,
                    ExternalProvider.Google,
                    "google-reconciliation-race"),
                CreateLogin(
                    user.Id,
                    shared.Id,
                    ExternalProvider.GitHub,
                    "github-reconciliation-shared"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            userId = user.Id;
            primaryEmailId = primary.Id;
            sharedEmailId = shared.Id;
        }

        await using var firstDb = CreateContext();
        await using var secondDb = CreateContext();
        await firstDb.Database.OpenConnectionAsync(
            TestContext.Current.CancellationToken);
        await secondDb.Database.OpenConnectionAsync(
            TestContext.Current.CancellationToken);
        var firstPid =
            ((NpgsqlConnection)firstDb.Database.GetDbConnection()).ProcessID;
        var secondPid =
            ((NpgsqlConnection)secondDb.Database.GetDbConnection()).ProcessID;
        Assert.NotEqual(firstPid, secondPid);
        var existingLoginRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStore = new PauseAfterExistingLoginStore(
            new EfExternalAccountStore(firstDb, TimeProvider.System),
            existingLoginRead,
            releaseFirstCallback.Task);
        var secondStore =
            new EfExternalAccountStore(secondDb, TimeProvider.System);
        var firstService = new ExternalIdentityService(
            firstStore,
            new EfApplicationUnitOfWork(firstDb),
            new FixedTimeProvider(Now));
        var secondService = new ExternalIdentityService(
            secondStore,
            new EfApplicationUnitOfWork(secondDb),
            new FixedTimeProvider(Now.AddMinutes(1)));
        var firstIncoming = Identity(
            ExternalProvider.Google,
            "google-reconciliation-race",
            "reconcile-race-incoming-b@example.test");
        var secondIncoming = Identity(
            ExternalProvider.Google,
            "google-reconciliation-race",
            "reconcile-race-incoming-c@example.test");

        var firstCallback = firstService.ReconcileAsync(
            firstIncoming,
            ExternalAuthIntent.SignIn,
            current: null,
            TestContext.Current.CancellationToken);
        await existingLoginRead.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var secondCallback = Task.Run(
            () => secondService.ReconcileAsync(
                secondIncoming,
                ExternalAuthIntent.SignIn,
                current: null,
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        var blockedOnFirstLoginLock = await WaitUntilBlockedAsync(
            secondPid,
            firstPid,
            TimeSpan.FromSeconds(5));
        var secondStayedIncomplete = !secondCallback.IsCompleted;
        releaseFirstCallback.TrySetResult();
        var firstResult = await firstCallback;
        var secondResult = await secondCallback;

        Assert.True(blockedOnFirstLoginLock);
        Assert.True(secondStayedIncomplete);
        Assert.Null(firstResult.Failure);
        Assert.NotNull(firstResult.Value);
        Assert.Null(secondResult.Failure);
        Assert.NotNull(secondResult.Value);

        await using var verify = CreateContext();
        var finalEmail = await (
            from login in verify.UserLogins.AsNoTracking()
            join email in verify.UserEmails.AsNoTracking()
                on login.VerifiedEmailId equals email.Id
            where login.UserId == userId
                && login.LoginProvider == ExternalProvider.Google.Value
                && login.ProviderKey == "google-reconciliation-race"
            select email.NormalizedEmail).SingleAsync(
                TestContext.Current.CancellationToken);
        Assert.Equal(secondIncoming.Email.NormalizedValue, finalEmail);
        Assert.False(await verify.UserEmails.AnyAsync(
            row => row.UserId == userId
                && (row.NormalizedEmail ==
                    firstIncoming.Email.NormalizedValue
                    || row.NormalizedEmail ==
                    "RECONCILE-RACE-OLD-A@EXAMPLE.TEST"),
            TestContext.Current.CancellationToken));
        Assert.True(await verify.UserEmails.AnyAsync(
            row => row.UserId == userId
                && row.NormalizedEmail ==
                secondIncoming.Email.NormalizedValue
                && !row.IsPrimary,
            TestContext.Current.CancellationToken));
        Assert.True(await verify.UserEmails.AnyAsync(
            row => row.Id == primaryEmailId && row.IsPrimary,
            TestContext.Current.CancellationToken));
        Assert.True(await verify.UserEmails.AnyAsync(
            row => row.Id == sharedEmailId && !row.IsPrimary,
            TestContext.Current.CancellationToken));
        Assert.True(await verify.UserLogins.AnyAsync(
            row => row.UserId == userId
                && row.LoginProvider == ExternalProvider.GitHub.Value
                && row.VerifiedEmailId == sharedEmailId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AccountStoreProjectsProfileEmailsConnectionsAndUpdatesName()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var user = CreateUser("profile@example.test");
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var primary = AddEmail(db, user, "profile@example.test", primary: true);
        var secondary = AddEmail(db, user, "secondary@example.test", primary: false);
        db.UserLogins.Add(CreateLogin(
            user.Id,
            secondary.Id,
            ExternalProvider.GitHub,
            "github-profile",
            Now.AddMinutes(2),
            Now.AddMinutes(3)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = new EfAccountStore(db, new FixedTimeProvider(Now.AddHours(1)));

        var snapshot = await store.GetAsync(
            new UserId(user.Id),
            TestContext.Current.CancellationToken);
        var connections = await store.ListConnectionsAsync(
            new UserId(user.Id),
            TestContext.Current.CancellationToken);
        var updated = await store.UpdateDisplayNameAsync(
            new UserId(user.Id),
            "Updated Profile",
            TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(primary.NormalizedEmail, snapshot.PrimaryEmail.NormalizedValue);
        Assert.Equal(2, snapshot.Emails.Count);
        Assert.Equal(
            [ExternalProvider.GitHub],
            snapshot.Emails.Single(email => !email.IsPrimary).Providers);
        var connection = Assert.Single(connections);
        Assert.Equal(ExternalProvider.GitHub, connection.Provider);
        Assert.Equal(secondary.NormalizedEmail, connection.Email?.NormalizedValue);
        Assert.Equal(Now.AddMinutes(2), connection.ConnectedAt);
        Assert.Equal(Now.AddMinutes(3), connection.LastUsedAt);
        Assert.NotNull(updated);
        Assert.Equal("Updated Profile", updated.User.Name);
        Assert.Equal(Now.AddHours(1), await db.Users
            .Where(row => row.Id == user.Id)
            .Select(row => row.UpdatedAt)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProfileUpdateReturnsMissingWhenConcurrentDeletionWon()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var store = new EfAccountStore(db, new FixedTimeProvider(Now));

        var updated = await store.UpdateDisplayNameAsync(
            new UserId(Guid.CreateVersion7()),
            "Already Deleted",
            TestContext.Current.CancellationToken);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DisconnectDeletesOnlyTargetedLoginAndItsOrphanSecondaryEmail()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var (user, _, secondary) = await SeedConnectedUserAsync(db);
        var google = CreateLogin(
            user.Id,
            secondary.Id,
            ExternalProvider.Google,
            "google-target");
        var github = CreateLogin(
            user.Id,
            (await db.UserEmails.SingleAsync(
                row => row.UserId == user.Id && row.IsPrimary,
                TestContext.Current.CancellationToken)).Id,
            ExternalProvider.GitHub,
            "github-preserve");
        db.UserLogins.AddRange(google, github);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = new EfAccountStore(db, TimeProvider.System);
        var snapshot = await store.GetDisconnectSnapshotAsync(
            new UserId(user.Id),
            ExternalProvider.Google,
            ConfiguredProviders,
            TestContext.Current.CancellationToken);

        await store.DisconnectAsync(
            Assert.IsType<DisconnectSnapshot>(snapshot),
            ConfiguredProviders,
            TestContext.Current.CancellationToken);

        Assert.False(await db.UserLogins.AnyAsync(
            row => row.LoginProvider == ExternalProvider.Google.Value,
            TestContext.Current.CancellationToken));
        Assert.True(await db.UserLogins.AnyAsync(
            row => row.LoginProvider == ExternalProvider.GitHub.Value,
            TestContext.Current.CancellationToken));
        Assert.False(await db.UserEmails.AnyAsync(
            row => row.Id == secondary.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisconnectAtomicallyRejectsWhenOnlyUnconfiguredLoginsWouldSurvive()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var (user, primary, secondary) = await SeedConnectedUserAsync(db);
        db.UserLogins.AddRange(
            CreateLogin(
                user.Id,
                secondary.Id,
                ExternalProvider.GitHub,
                "github-configured-candidate"),
            CreateLogin(
                user.Id,
                primary.Id,
                ExternalProvider.Google,
                "google-unconfigured-current"),
            CreateLogin(
                user.Id,
                primary.Id,
                ExternalProvider.Vk,
                "vk-unconfigured-extra"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = new EfAccountStore(db, TimeProvider.System);
        var configured = new[] { ExternalProvider.GitHub };
        var snapshot = Assert.IsType<DisconnectSnapshot>(
            await store.GetDisconnectSnapshotAsync(
                new UserId(user.Id),
                ExternalProvider.GitHub,
                configured,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, snapshot.ConfiguredSurvivorCount);
        await Assert.ThrowsAsync<AccountConcurrencyException>(() =>
            store.DisconnectAsync(
                snapshot,
                configured,
                TestContext.Current.CancellationToken));

        db.ChangeTracker.Clear();
        Assert.Equal(
            ["github", "google", "vk"],
            await db.UserLogins.AsNoTracking()
                .Where(row => row.UserId == user.Id)
                .OrderBy(row => row.LoginProvider)
                .Select(row => row.LoginProvider)
                .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.True(await db.UserEmails.AnyAsync(
            row => row.Id == secondary.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisconnectAtomicallyAllowsRemovalWhenConfiguredLoginSurvives()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var (user, primary, secondary) = await SeedConnectedUserAsync(db);
        db.UserLogins.AddRange(
            CreateLogin(
                user.Id,
                secondary.Id,
                ExternalProvider.GitHub,
                "github-configured-candidate"),
            CreateLogin(
                user.Id,
                primary.Id,
                ExternalProvider.Google,
                "google-configured-survivor"),
            CreateLogin(
                user.Id,
                primary.Id,
                ExternalProvider.Vk,
                "vk-unconfigured-extra"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = new EfAccountStore(db, TimeProvider.System);
        var configured = new[]
        {
            ExternalProvider.GitHub,
            ExternalProvider.Google
        };
        var snapshot = Assert.IsType<DisconnectSnapshot>(
            await store.GetDisconnectSnapshotAsync(
                new UserId(user.Id),
                ExternalProvider.GitHub,
                configured,
                TestContext.Current.CancellationToken));

        Assert.Equal(1, snapshot.ConfiguredSurvivorCount);
        await store.DisconnectAsync(
            snapshot,
            configured,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["google", "vk"],
            await db.UserLogins.AsNoTracking()
                .Where(row => row.UserId == user.Id)
                .OrderBy(row => row.LoginProvider)
                .Select(row => row.LoginProvider)
                .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.UserEmails.AnyAsync(
            row => row.Id == secondary.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisconnectPreservesPrimaryAndSharedSecondaryEmails()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var (user, primary, secondary) = await SeedConnectedUserAsync(db);
        db.UserLogins.AddRange(
            CreateLogin(
                user.Id,
                primary.Id,
                ExternalProvider.Google,
                "google-primary"),
            CreateLogin(
                user.Id,
                secondary.Id,
                ExternalProvider.GitHub,
                "github-shared"),
            CreateLogin(
                user.Id,
                secondary.Id,
                ExternalProvider.GitLab,
                "gitlab-shared"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = new EfAccountStore(db, TimeProvider.System);

        var primarySnapshot = await store.GetDisconnectSnapshotAsync(
            new UserId(user.Id),
            ExternalProvider.Google,
            ConfiguredProviders,
            TestContext.Current.CancellationToken);
        await store.DisconnectAsync(
            Assert.IsType<DisconnectSnapshot>(primarySnapshot),
            ConfiguredProviders,
            TestContext.Current.CancellationToken);

        var sharedSnapshot = await store.GetDisconnectSnapshotAsync(
            new UserId(user.Id),
            ExternalProvider.GitHub,
            ConfiguredProviders,
            TestContext.Current.CancellationToken);
        await store.DisconnectAsync(
            Assert.IsType<DisconnectSnapshot>(sharedSnapshot),
            ConfiguredProviders,
            TestContext.Current.CancellationToken);

        Assert.True(await db.UserEmails.AnyAsync(
            row => row.Id == primary.Id,
            TestContext.Current.CancellationToken));
        Assert.True(await db.UserEmails.AnyAsync(
            row => row.Id == secondary.Id,
            TestContext.Current.CancellationToken));
        Assert.True(await db.UserLogins.AnyAsync(
            row => row.LoginProvider == ExternalProvider.GitLab.Value,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisconnectRejectsStaleLockedSnapshotWithoutPartialMutation()
    {
        await MigrateAsync();
        Guid userId;
        Guid emailId;
        DisconnectSnapshot snapshot;
        await using (var setup = CreateContext())
        {
            var (user, primary, secondary) = await SeedConnectedUserAsync(setup);
            userId = user.Id;
            emailId = secondary.Id;
            setup.UserLogins.AddRange(
                CreateLogin(
                    user.Id,
                    secondary.Id,
                    ExternalProvider.Google,
                    "google-stale"),
                CreateLogin(
                    user.Id,
                    primary.Id,
                    ExternalProvider.GitHub,
                    "github-stable"));
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            var setupStore = new EfAccountStore(setup, TimeProvider.System);
            snapshot = Assert.IsType<DisconnectSnapshot>(
                await setupStore.GetDisconnectSnapshotAsync(
                    new UserId(user.Id),
                    ExternalProvider.Google,
                    ConfiguredProviders,
                    TestContext.Current.CancellationToken));
        }

        await using (var concurrent = CreateContext())
        {
            var login = await concurrent.UserLogins.SingleAsync(
                row => row.UserId == userId
                    && row.LoginProvider == ExternalProvider.Google.Value,
                TestContext.Current.CancellationToken);
            var primaryId = await concurrent.UserEmails
                .Where(row => row.UserId == userId && row.IsPrimary)
                .Select(row => row.Id)
                .SingleAsync(TestContext.Current.CancellationToken);
            login.VerifiedEmailId = primaryId;
            await concurrent.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var db = CreateContext();
        var store = new EfAccountStore(db, TimeProvider.System);
        await Assert.ThrowsAsync<AccountConcurrencyException>(() =>
            store.DisconnectAsync(
                snapshot,
                ConfiguredProviders,
                TestContext.Current.CancellationToken));

        Assert.True(await db.UserLogins.AnyAsync(
            row => row.UserId == userId
                && row.LoginProvider == ExternalProvider.Google.Value,
            TestContext.Current.CancellationToken));
        Assert.True(await db.UserEmails.AnyAsync(
            row => row.Id == emailId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisconnectRollsBackLoginAndEmailTogetherWhenEmailDeleteFails()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var (user, primary, secondary) = await SeedConnectedUserAsync(db);
        db.UserLogins.AddRange(
            CreateLogin(
                user.Id,
                secondary.Id,
                ExternalProvider.Google,
                "google-rollback"),
            CreateLogin(
                user.Id,
                primary.Id,
                ExternalProvider.GitHub,
                "github-rollback"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE FUNCTION auth.reject_test_email_delete()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                RAISE EXCEPTION 'test email delete rejected';
            END;
            $$;
            CREATE TRIGGER reject_test_email_delete
            BEFORE DELETE ON auth.user_emails
            FOR EACH ROW
            EXECUTE FUNCTION auth.reject_test_email_delete();
            """,
            TestContext.Current.CancellationToken);
        var store = new EfAccountStore(db, TimeProvider.System);
        var snapshot = Assert.IsType<DisconnectSnapshot>(
            await store.GetDisconnectSnapshotAsync(
                new UserId(user.Id),
                ExternalProvider.Google,
                ConfiguredProviders,
                TestContext.Current.CancellationToken));

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            store.DisconnectAsync(
                snapshot,
                ConfiguredProviders,
                TestContext.Current.CancellationToken));
        Assert.Equal("P0001", exception.SqlState);
        Assert.Equal("test email delete rejected", exception.MessageText);

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.UserLogins.CountAsync(
            row => row.UserId == user.Id
                && row.LoginProvider == ExternalProvider.Google.Value
                && row.ProviderKey == "google-rollback",
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.UserEmails.CountAsync(
            row => row.Id == secondary.Id
                && row.UserId == user.Id
                && row.NormalizedEmail == secondary.NormalizedEmail,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AccountDeleteCascadesEmailLoginAndSessionRows()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var (user, primary, _) = await SeedConnectedUserAsync(db);
        db.UserLogins.Add(CreateLogin(
            user.Id,
            primary.Id,
            ExternalProvider.Google,
            "google-delete"));
        db.Sessions.Add(CreateSession(user.Id, Now, 1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = new EfAccountStore(db, TimeProvider.System);

        await store.DeleteAsync(
            new UserId(user.Id),
            TestContext.Current.CancellationToken);

        Assert.False(await db.Users.AnyAsync(
            row => row.Id == user.Id,
            TestContext.Current.CancellationToken));
        Assert.False(await db.UserEmails.AnyAsync(
            row => row.UserId == user.Id,
            TestContext.Current.CancellationToken));
        Assert.False(await db.UserLogins.AnyAsync(
            row => row.UserId == user.Id,
            TestContext.Current.CancellationToken));
        Assert.False(await db.Sessions.AnyAsync(
            row => row.UserId == user.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SessionStorePagesWithoutExposingTicketsAndQualifiesDeletesByOwner()
    {
        await MigrateAsync();
        await using var db = CreateContext();
        var owner = CreateUser("sessions@example.test");
        var other = CreateUser("other-sessions@example.test");
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var oldest = CreateSession(owner.Id, Now, 1);
        var middle = CreateSession(owner.Id, Now.AddMinutes(1), 2);
        var newest = CreateSession(owner.Id, Now.AddMinutes(2), 3);
        var foreign = CreateSession(other.Id, Now.AddMinutes(3), 4);
        db.Sessions.AddRange(oldest, middle, newest, foreign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = new EfAccountSessionStore(db, new FixedTimeProvider(Now));

        var firstPage = await store.ListAsync(
            new UserId(owner.Id),
            cursor: null,
            limit: 2,
            TestContext.Current.CancellationToken);
        Assert.Equal([new SessionId(newest.Id), new SessionId(middle.Id)],
            firstPage.Items.Select(row => row.Id));
        Assert.All(firstPage.Items, row => Assert.Equal("local", row.AuthenticationMethod));
        Assert.DoesNotContain(
            Convert.ToHexString(newest.ProtectedTicket),
            System.Text.Json.JsonSerializer.Serialize(firstPage),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(SessionCursor.TryDecode(
            Assert.IsType<string>(firstPage.NextCursor),
            out var cursor));

        var secondPage = await store.ListAsync(
            new UserId(owner.Id),
            cursor,
            limit: 2,
            TestContext.Current.CancellationToken);
        Assert.Equal([new SessionId(oldest.Id)], secondPage.Items.Select(row => row.Id));
        Assert.Null(secondPage.NextCursor);

        Assert.False(await store.RevokeAsync(
            new UserId(owner.Id),
            new SessionId(foreign.Id),
            TestContext.Current.CancellationToken));
        Assert.True(await store.RevokeAsync(
            new UserId(owner.Id),
            new SessionId(oldest.Id),
            TestContext.Current.CancellationToken));
        var revoked = await store.RevokeOthersAsync(
            new UserId(owner.Id),
            new SessionId(newest.Id),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, revoked);
        Assert.True(await db.Sessions.AnyAsync(
            row => row.Id == newest.Id,
            TestContext.Current.CancellationToken));
        Assert.True(await db.Sessions.AnyAsync(
            row => row.Id == foreign.Id,
            TestContext.Current.CancellationToken));
    }

    private async Task MigrateAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    private TemplateDbContext CreateContext() => CreateContext(_connectionString);

    private static TemplateDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        TemplateDbContext.Configure(options, connectionString);
        return new TemplateDbContext(options.Options);
    }

    private static ApplicationUser CreateUser(string email) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = email.Split('@')[0],
            CreatedAt = Now,
            UpdatedAt = Now
        };

    private static UserEmailEntity AddEmail(
        TemplateDbContext db,
        ApplicationUser user,
        string email,
        bool primary)
    {
        var entity = new UserEmailEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            IsPrimary = primary,
            CreatedAt = Now
        };
        db.UserEmails.Add(entity);
        return entity;
    }

    private static ApplicationUserLogin CreateLogin(
        Guid userId,
        Guid emailId,
        ExternalProvider provider,
        string subject,
        DateTimeOffset? connectedAt = null,
        DateTimeOffset? lastUsedAt = null) =>
        new()
        {
            UserId = userId,
            LoginProvider = provider.Value,
            ProviderKey = subject,
            ProviderDisplayName = provider.Value,
            VerifiedEmailId = emailId,
            ConnectedAt = connectedAt ?? Now,
            LastUsedAt = lastUsedAt
        };

    private static AuthSessionEntity CreateSession(
        Guid userId,
        DateTimeOffset updatedAt,
        byte discriminator) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TicketKeyHash = Enumerable.Repeat(discriminator, 32).ToArray(),
            ProtectedTicket = [discriminator, 0xCA, 0xFE],
            CreatedAt = updatedAt.AddMinutes(-1),
            UpdatedAt = updatedAt,
            ExpiresAt = updatedAt.AddDays(7),
            AuthenticationMethod = BrowserAuthenticationMethods.Local,
            IpAddress = System.Net.IPAddress.Parse($"192.0.2.{discriminator}"),
            UserAgent = $"agent-{discriminator}"
        };

    private static ExternalIdentity Identity(
        ExternalProvider provider,
        string subject,
        string email,
        string? displayName = null,
        string? imageUrl = null) =>
        new(
            provider,
            subject,
            VerifiedEmail.Create(email),
            displayName,
            imageUrl is null ? null : new Uri(imageUrl));

    private static async Task<(ApplicationUser User, UserEmailEntity Primary, UserEmailEntity Secondary)>
        SeedConnectedUserAsync(TemplateDbContext db)
    {
        var user = CreateUser($"connected-{Guid.NewGuid():N}@example.test");
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var primary = AddEmail(db, user, user.Email!, primary: true);
        var secondary = AddEmail(
            db,
            user,
            $"secondary-{Guid.NewGuid():N}@example.test",
            primary: false);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (user, primary, secondary);
    }

    private static async Task AssertUniqueViolationAsync(TemplateDbContext db)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(
        NpgsqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async Task<bool> WaitUntilBlockedAsync(
        int blockedPid,
        int blockingPid,
        TimeSpan timeout)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT @blockingPid = ANY(pg_blocking_pids(@blockedPid))";
            command.Parameters.AddWithValue("blockingPid", blockingPid);
            command.Parameters.AddWithValue("blockedPid", blockedPid);
            if (Assert.IsType<bool>(await command.ExecuteScalarAsync(
                    TestContext.Current.CancellationToken)))
            {
                return true;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(20),
                TestContext.Current.CancellationToken);
        }

        return false;
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PauseAfterExistingLoginStore(
        IExternalAccountStore inner,
        TaskCompletionSource existingLoginRead,
        Task release)
        : IExternalAccountStore
    {
        public async Task<ExternalLoginSnapshot?> FindLoginAsync(
            ExternalProvider provider,
            string subject,
            CancellationToken ct)
        {
            var login = await inner.FindLoginAsync(provider, subject, ct);
            if (login is not null)
            {
                existingLoginRead.TrySetResult();
                await release.WaitAsync(ct);
            }

            return login;
        }

        public Task<AuthUser?> FindUserByEmailAsync(
            string normalizedEmail,
            CancellationToken ct) =>
            inner.FindUserByEmailAsync(normalizedEmail, ct);

        public Task<bool> IsEmailVouchedAsync(
            UserId userId,
            string normalizedEmail,
            CancellationToken ct) =>
            inner.IsEmailVouchedAsync(userId, normalizedEmail, ct);

        public Task<AuthUser> CreateUserAsync(
            ExternalIdentity identity,
            CancellationToken ct) =>
            inner.CreateUserAsync(identity, ct);

        public Task EnsureVerifiedEmailAsync(
            UserId userId,
            VerifiedEmail email,
            bool primary,
            CancellationToken ct) =>
            inner.EnsureVerifiedEmailAsync(userId, email, primary, ct);

        public Task AddLoginAsync(
            UserId userId,
            ExternalIdentity identity,
            DateTimeOffset connectedAt,
            bool usedForSignIn,
            CancellationToken ct) =>
            inner.AddLoginAsync(
                userId,
                identity,
                connectedAt,
                usedForSignIn,
                ct);

        public Task UpdateLoginEmailAsync(
            UserId userId,
            ExternalIdentity identity,
            DateTimeOffset? usedAt,
            CancellationToken ct) =>
            inner.UpdateLoginEmailAsync(userId, identity, usedAt, ct);

        public Task UpdateLinkedProfileAsync(
            UserId userId,
            string? displayName,
            Uri? imageUrl,
            CancellationToken ct) =>
            inner.UpdateLinkedProfileAsync(userId, displayName, imageUrl, ct);
    }
}
