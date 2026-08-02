using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Template.Api.Tests.Infrastructure;
using Template.Application.Organizations;
using Template.Application.Organizations.Ports;
using Template.Domain.Authentication;
using Template.Domain.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Organizations;

public sealed class OrganizationEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task OrganizationLifecycleUsesExactEnvelopesLocationsAndSessionProjection()
    {
        using var client = factory.CreateApiClient();
        var actor = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Organization Owner",
            "local-agent+organization-owner@local-agent.test");

        using var firstCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "First Workspace");
        var first = await OrganizationEndpointTestSupport.ReadDataAsync(firstCreate);
        var firstId = first.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, firstCreate.StatusCode);
        Assert.Equal(
            "/api/v1/organizations/by-key/first-workspace",
            firstCreate.Headers.Location?.OriginalString);
        AssertOrganizationDetail(
            first,
            firstId,
            "First Workspace",
            "first-workspace",
            "owner",
            canUpdate: true,
            canDelete: true);

        using var secondCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Second Workspace");
        var second = await OrganizationEndpointTestSupport.ReadDataAsync(secondCreate);
        var secondId = second.GetProperty("id").GetGuid();

        using var list = await client.GetAsync(
            "/api/v1/organizations?limit=50",
            TestContext.Current.CancellationToken);
        var listData = await OrganizationEndpointTestSupport.ReadDataAsync(list);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(2, listData.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, listData.GetProperty("nextCursor").ValueKind);

        using var detail = await client.GetAsync(
            $"/api/v1/organizations/by-key/{firstId:D}",
            TestContext.Current.CancellationToken);
        var detailData = await OrganizationEndpointTestSupport.ReadDataAsync(detail);
        AssertOrganizationDetail(
            detailData,
            firstId,
            "First Workspace",
            "first-workspace",
            "owner",
            canUpdate: true,
            canDelete: true);

        using var update = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/v1/organizations/{firstId:D}",
            new
            {
                name = "  Renamed Workspace  ",
                slug = "  RENAMED-WORKSPACE  ",
                allowedEmailDomains = new[]
                {
                    "@Example.COM",
                    "engineering.example.com",
                    "example.com"
                }
            });
        var updateData = await OrganizationEndpointTestSupport.ReadDataAsync(update);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        AssertOrganizationDetail(
            updateData,
            firstId,
            "Renamed Workspace",
            "renamed-workspace",
            "owner",
            canUpdate: true,
            canDelete: true);
        Assert.Equal(
            ["engineering.example.com", "example.com"],
            updateData.GetProperty("allowedEmailDomains")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());

        using var setActive = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            "/api/v1/auth/session/active-organization",
            new { organizationId = firstId });
        var activeData = await OrganizationEndpointTestSupport.ReadDataAsync(setActive);
        Assert.Equal(HttpStatusCode.OK, setActive.StatusCode);
        Assert.Equal(firstId, activeData.GetProperty("organizationId").GetGuid());
        Assert.DoesNotContain(
            setActive.Headers.TryGetValues("Set-Cookie", out var activeCookies)
                ? activeCookies
                : [],
            value => value.StartsWith(
                "__Host-template.session=",
                StringComparison.Ordinal));

        using var session = await client.GetAsync(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var sessionData = await OrganizationEndpointTestSupport.ReadDataAsync(session);
        Assert.True(sessionData.GetProperty("authenticated").GetBoolean());
        Assert.Equal(
            firstId,
            sessionData.GetProperty("session")
                .GetProperty("activeOrganizationId")
                .GetGuid());
        Assert.Equal(actor.UserId, sessionData.GetProperty("user").GetProperty("id").GetGuid());

        using var delete = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/organizations/{secondId:D}",
            new { confirmationName = "Second Workspace" });
        var deletionData = await OrganizationEndpointTestSupport.ReadDataAsync(delete);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(secondId, deletionData.GetProperty("organizationId").GetGuid());

        OrganizationEndpointTestSupport.AssertNoStore(
            firstCreate,
            secondCreate,
            list,
            detail,
            update,
            setActive,
            session,
            delete);
    }

    [Fact]
    public async Task Organization_cursor_remains_complete_when_a_later_page_is_renamed()
    {
        using var client = factory.CreateApiClient();
        var actor = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Pagination Owner",
            "local-agent+organization-pagination@local-agent.test");
        using var alphaCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Alpha");
        using var bravoCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Bravo");
        using var zuluCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Zulu");
        var alphaId = (await OrganizationEndpointTestSupport.ReadDataAsync(alphaCreate))
            .GetProperty("id")
            .GetGuid();
        var bravoId = (await OrganizationEndpointTestSupport.ReadDataAsync(bravoCreate))
            .GetProperty("id")
            .GetGuid();
        var zuluId = (await OrganizationEndpointTestSupport.ReadDataAsync(zuluCreate))
            .GetProperty("id")
            .GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            foreach (var (organizationId, joinedAt) in new[]
                     {
                         (alphaId, DateTimeOffset.Parse("2026-07-30T10:00:00Z")),
                         (bravoId, DateTimeOffset.Parse("2026-07-30T11:00:00Z")),
                         (zuluId, DateTimeOffset.Parse("2026-07-30T12:00:00Z"))
                     })
            {
                await db.OrganizationMembers
                    .Where(member =>
                        member.OrganizationId == organizationId &&
                        member.UserId == actor.UserId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            member => member.JoinedAt,
                            joinedAt),
                        TestContext.Current.CancellationToken);
            }
        }

        using var firstResponse = await client.GetAsync(
            "/api/v1/organizations?limit=2",
            TestContext.Current.CancellationToken);
        var first = await OrganizationEndpointTestSupport.ReadDataAsync(firstResponse);
        var cursor = first.GetProperty("nextCursor").GetString();
        using var rename = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/v1/organizations/{zuluId:D}",
            new { name = "Aardvark" });
        using var secondResponse = await client.GetAsync(
            $"/api/v1/organizations?limit=2&cursor={Uri.EscapeDataString(cursor!)}",
            TestContext.Current.CancellationToken);
        var second = await OrganizationEndpointTestSupport.ReadDataAsync(secondResponse);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal([alphaId, bravoId], first.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid()));
        Assert.NotNull(cursor);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var continuation = Assert.Single(second.GetProperty("items").EnumerateArray());
        Assert.Equal(zuluId, continuation.GetProperty("id").GetGuid());
        Assert.Equal("Aardvark", continuation.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        OrganizationEndpointTestSupport.AssertNoStore(
            alphaCreate,
            bravoCreate,
            zuluCreate,
            firstResponse,
            rename,
            secondResponse);
    }

    [Fact]
    public async Task MembershipLifecycleReturnsClosedSafeMemberProjection()
    {
        using var ownerClient = factory.CreateApiClient();
        var owner = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Member Directory Owner",
            "local-agent+member-directory-owner@local-agent.test");
        using var targetClient = factory.CreateApiClient();
        var target = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            targetClient,
            "Member Directory Target",
            "local-agent+member-directory-target@local-agent.test");
        using var created =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Directory Workspace");
        var organizationId = (await OrganizationEndpointTestSupport.ReadDataAsync(created))
            .GetProperty("id")
            .GetGuid();

        using var add = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/members",
            new { userId = target.UserId, role = "member" });
        var added = await OrganizationEndpointTestSupport.ReadDataAsync(add);
        var memberId = added.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Equal(
            $"/api/v1/organizations/{organizationId:D}/members/{memberId:D}",
            add.Headers.Location?.OriginalString);
        AssertMember(
            added,
            memberId,
            target.UserId,
            "Member Directory Target",
            target.Email,
            "member");
        using var memberDetail = await targetClient.GetAsync(
            "/api/v1/organizations/by-key/directory-workspace",
            TestContext.Current.CancellationToken);
        Assert.False((await OrganizationEndpointTestSupport.ReadDataAsync(memberDetail))
            .GetProperty("capabilities")
            .GetProperty("canManageApiKeys")
            .GetBoolean());

        using var list = await ownerClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/members?limit=50",
            TestContext.Current.CancellationToken);
        var page = await OrganizationEndpointTestSupport.ReadDataAsync(list);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var members = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, members.Length);
        Assert.Contains(
            members,
            item => item.GetProperty("userId").GetGuid() == owner.UserId);
        Assert.Contains(
            members,
            item => item.GetProperty("userId").GetGuid() == target.UserId);

        using var roleUpdate =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/members/{memberId:D}",
                new { role = "admin" });
        var updated = await OrganizationEndpointTestSupport.ReadDataAsync(roleUpdate);
        Assert.Equal(HttpStatusCode.OK, roleUpdate.StatusCode);
        AssertMember(
            updated,
            memberId,
            target.UserId,
            "Member Directory Target",
            target.Email,
            "admin");
        using var adminDetail = await targetClient.GetAsync(
            "/api/v1/organizations/by-key/directory-workspace",
            TestContext.Current.CancellationToken);
        Assert.True((await OrganizationEndpointTestSupport.ReadDataAsync(adminDetail))
            .GetProperty("capabilities")
            .GetProperty("canManageApiKeys")
            .GetBoolean());

        OrganizationEndpointTestSupport.AssertNoStore(
            add,
            memberDetail,
            list,
            roleUpdate,
            adminDetail);
    }

    [Fact]
    public async Task MemberListDoesNotProjectCredentialsFromAnHttpsAvatarUrl()
    {
        using var client = factory.CreateApiClient();
        var owner = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Credential Avatar Owner",
            "local-agent+credential-avatar-owner@local-agent.test");
        using var created =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Credential Avatar Workspace");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(created))
            .GetProperty("id")
            .GetGuid();
        const string credentialedAvatar =
            "https://user:secret@cdn.example.test/avatar.png";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var changed = await scope.ServiceProvider
                .GetRequiredService<TemplateDbContext>()
                .Users
                .Where(user => user.Id == owner.UserId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.ImageUrl,
                        credentialedAvatar),
                    TestContext.Current.CancellationToken);
            Assert.Equal(1, changed);
        }

        using var response = await client.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/members",
            TestContext.Current.CancellationToken);
        var data = await OrganizationEndpointTestSupport.ReadDataAsync(response);
        var member = Assert.Single(
            data.GetProperty("items")
                .EnumerateArray(),
            item => item.GetProperty("userId").GetGuid() == owner.UserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            JsonValueKind.Null,
            member.GetProperty("imageUrl").ValueKind);
        Assert.DoesNotContain(
            credentialedAvatar,
            data.GetRawText(),
            StringComparison.Ordinal);
        OrganizationEndpointTestSupport.AssertNoStore(response);
    }

    [Fact]
    public async Task OrganizationPatchRejectsAUuidShapedSlugAtTheHttpBoundary()
    {
        using var ownerClient = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Exact ID Owner",
            "local-agent+exact-id-owner@local-agent.test");
        using var created =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Exact ID Accessible");
        var accessibleId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(created))
            .GetProperty("id")
            .GetGuid();

        var requestedId = Guid.NewGuid();

        using var slug = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Patch,
            $"/api/v1/organizations/{accessibleId:D}",
            new { slug = requestedId.ToString("D") });
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            slug,
            "slug");
        OrganizationEndpointTestSupport.AssertNoStore(slug);
    }

    [Fact]
    public async Task MemberListMapsAtomicStoreNotFoundWithoutASeparateKeyPrecheck()
    {
        var store = new DisappearingMemberListStore();
        await using var isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrganizationStore>();
                services.AddSingleton<IOrganizationStore>(store);
            }));
        using var client = isolated.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Disappearing List Owner",
            "local-agent+disappearing-list-owner@local-agent.test");

        using var response = await client.GetAsync(
            $"/api/v1/organizations/{Guid.NewGuid():D}/members",
            TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "organization_not_found");
        Assert.Equal(0, store.GetByKeyCalls);
        Assert.Equal(1, store.ListMembersCalls);
        OrganizationEndpointTestSupport.AssertNoStore(response);
    }

    [Fact]
    public async Task OrganizationDetailMapsStoreConcurrencyConflictToThePublishedProblem()
    {
        var store = new ConcurrencyOrganizationDetailStore();
        await using var isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrganizationStore>();
                services.AddSingleton<IOrganizationStore>(store);
            }));
        using var client = isolated.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Concurrency Detail Owner",
            "local-agent+concurrency-detail-owner@local-agent.test");

        using var response = await client.GetAsync(
            "/api/v1/organizations/by-key/concurrency-detail",
            TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "concurrency_conflict");
        Assert.Equal(1, store.GetByKeyCalls);
        OrganizationEndpointTestSupport.AssertNoStore(response);
    }

    [Fact]
    public async Task OrganizationPatchRejectsASlugOver64CharactersAtTheHttpBoundary()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Long Slug Owner",
            "local-agent+long-slug-owner@local-agent.test");
        using var created =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Long Slug Workspace");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(created))
            .GetProperty("id")
            .GetGuid();

        using var response =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                client,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}",
                new { slug = new string('a', 65) });

        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            response,
            "slug");
        OrganizationEndpointTestSupport.AssertNoStore(response);
    }

    [Fact]
    public async Task DomainAcknowledgementProblemIsExplicitAndRetryCreatesExactlyOneMember()
    {
        using var ownerClient = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Domain Owner",
            "local-agent+domain-owner@local-agent.test");
        using var targetClient = factory.CreateApiClient();
        var target = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            targetClient,
            "Outside Domain",
            "local-agent+outside-domain@local-agent.test");
        using var created =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Domain Workspace");
        var organizationId = (await OrganizationEndpointTestSupport.ReadDataAsync(created))
            .GetProperty("id")
            .GetGuid();
        using var policy = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Patch,
            $"/api/v1/organizations/{organizationId:D}",
            new { allowedEmailDomains = new[] { "allowed.example" } });
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);

        using var warning = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/members",
            new
            {
                userId = target.UserId,
                role = "member",
                acknowledgeDomainRestriction = false
            });
        using var warningDocument = JsonDocument.Parse(
            await warning.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, warning.StatusCode);
        Assert.Equal(
            "member_domain_acknowledgement_required",
            warningDocument.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            target.Email,
            warningDocument.RootElement.GetProperty("email").GetString());
        Assert.Equal(
            "local-agent.test",
            warningDocument.RootElement.GetProperty("emailDomain").GetString());
        Assert.Equal(
            ["allowed.example"],
            warningDocument.RootElement.GetProperty("allowedEmailDomains")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
        Assert.Equal(
            1,
            await OrganizationEndpointTestSupport.CountMembersAsync(
                factory.Services,
                organizationId));

        using var accepted = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/members",
            new
            {
                userId = target.UserId,
                role = "member",
                acknowledgeDomainRestriction = true
            });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(
            2,
            await OrganizationEndpointTestSupport.CountMembersAsync(
                factory.Services,
                organizationId));

        using var duplicate = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/members",
            new
            {
                userId = target.UserId,
                role = "member",
                acknowledgeDomainRestriction = true
            });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            duplicate,
            HttpStatusCode.Conflict,
            "member_already_exists");
        Assert.Equal(
            2,
            await OrganizationEndpointTestSupport.CountMembersAsync(
                factory.Services,
                organizationId));
        OrganizationEndpointTestSupport.AssertNoStore(warning, accepted, duplicate);
    }

    [Fact]
    public async Task CollectionValidationAndOrganizationConflictsUseStableProblems()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Validation Owner",
            "local-agent+organization-validation@local-agent.test");
        using var created =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Only Workspace");
        var organizationId = (await OrganizationEndpointTestSupport.ReadDataAsync(created))
            .GetProperty("id")
            .GetGuid();

        using var invalidLimit = await client.GetAsync(
            "/api/v1/organizations?limit=101",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            invalidLimit,
            "limit");
        using var invalidCursor = await client.GetAsync(
            "/api/v1/organizations?cursor=not-a-valid-cursor",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            invalidCursor,
            HttpStatusCode.BadRequest,
            "invalid_cursor");
        var nulCursor = CreateChecksumValidOrganizationCursor(
            "\0",
            organizationId);
        using var invalidDatabaseCursor = await client.GetAsync(
            $"/api/v1/organizations?cursor={nulCursor}",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            invalidDatabaseCursor,
            HttpStatusCode.BadRequest,
            "invalid_cursor");
        using var invalidMemberLimit = await client.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/members?limit=0",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            invalidMemberLimit,
            "limit");

        using var emptyPatch = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/v1/organizations/{organizationId:D}",
            new { });
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            emptyPatch,
            "body");
        using var invalidSlug = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/v1/organizations/{organizationId:D}",
            new { slug = "not valid" });
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            invalidSlug,
            "slug");
        using var invalidDomain =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                client,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}",
                new { allowedEmailDomains = new[] { "not-a-domain" } });
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            invalidDomain,
            "allowedEmailDomains");
        using var badPath = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            "/api/v1/organizations/not-a-uuid",
            new { name = "Valid Name" });
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            badPath,
            "organizationId");
        using var mismatch = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/organizations/{organizationId:D}",
            new { confirmationName = "only workspace" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            mismatch,
            HttpStatusCode.BadRequest,
            "organization_confirmation_mismatch");
        using var last = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/organizations/{organizationId:D}",
            new { confirmationName = "Only Workspace" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            last,
            HttpStatusCode.Conflict,
            "last_organization_required");
    }

    [Fact]
    public async Task OrganizationNameRuntimeAcceptsDecimalDigitsButRejectsOtherNumbers()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Name Category Owner",
            "local-agent+organization-name-category@local-agent.test");

        using var decimalDigit =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Workspace ٣");
        Assert.Equal(HttpStatusCode.Created, decimalDigit.StatusCode);

        foreach (var invalidName in new[] { "Workspace Ⅻ", "Workspace ²" })
        {
            using var invalid =
                await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                    client,
                    HttpMethod.Post,
                    "/api/v1/organizations",
                    new { name = invalidName });
            await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
                invalid,
                "name");
        }
    }

    [Fact]
    public async Task OrganizationUpdateBoundsRawAllowedEmailDomainsAtOneHundred()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Domain Limit Owner",
            "local-agent+organization-domain-limit@local-agent.test");
        using var create =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Domain Limit");
        var organizationId = (await OrganizationEndpointTestSupport.ReadDataAsync(
            create)).GetProperty("id").GetGuid();
        var acceptedDomains = Enumerable.Range(0, 100)
            .Select(index => $"accepted-{index:D3}.example")
            .ToArray();

        using var accepted =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                client,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}",
                new { allowedEmailDomains = acceptedDomains });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(
            acceptedDomains.Order(StringComparer.Ordinal),
            (await OrganizationEndpointTestSupport.ReadDataAsync(accepted))
                .GetProperty("allowedEmailDomains")
                .EnumerateArray()
                .Select(value => value.GetString()!));

        var rejectedDomains = Enumerable
            .Repeat("repeated.example", 101)
            .ToArray();
        using var rejected =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                client,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}",
                new { allowedEmailDomains = rejectedDomains });
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            rejected,
            "allowedEmailDomains");

        using var detailResponse = await client.GetAsync(
            "/api/v1/organizations/by-key/domain-limit",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(
            acceptedDomains.Order(StringComparer.Ordinal),
            (await OrganizationEndpointTestSupport.ReadDataAsync(detailResponse))
                .GetProperty("allowedEmailDomains")
                .EnumerateArray()
                .Select(value => value.GetString()!));
    }

    private static void AssertOrganizationDetail(
        JsonElement data,
        Guid id,
        string name,
        string slug,
        string role,
        bool canUpdate,
        bool canDelete)
    {
        Assert.Equal(id, data.GetProperty("id").GetGuid());
        Assert.Equal(name, data.GetProperty("name").GetString());
        Assert.Equal(slug, data.GetProperty("slug").GetString());
        Assert.Equal(slug, data.GetProperty("canonicalKey").GetString());
        Assert.Equal(role, data.GetProperty("currentRole").GetString());
        Assert.NotEqual(default, data.GetProperty("createdAt").GetDateTimeOffset());
        Assert.NotEqual(default, data.GetProperty("updatedAt").GetDateTimeOffset());
        var capabilities = data.GetProperty("capabilities");
        Assert.Equal(
            canUpdate,
            capabilities.GetProperty("canUpdateOrganization").GetBoolean());
        Assert.Equal(
            canDelete,
            capabilities.GetProperty("canDeleteOrganization").GetBoolean());
        Assert.True(capabilities.GetProperty("canAddMembers").GetBoolean());
        Assert.True(capabilities.GetProperty("canUpdateMemberRoles").GetBoolean());
        Assert.Equal(
            role is "owner" or "admin",
            capabilities.GetProperty("canManageApiKeys").GetBoolean());
        Assert.Equal(JsonValueKind.Array, data.GetProperty("allowedEmailDomains").ValueKind);
    }

    private static void AssertMember(
        JsonElement data,
        Guid id,
        Guid userId,
        string name,
        string email,
        string role)
    {
        Assert.Equal(id, data.GetProperty("id").GetGuid());
        Assert.Equal(userId, data.GetProperty("userId").GetGuid());
        Assert.Equal(name, data.GetProperty("name").GetString());
        Assert.Equal(email, data.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("imageUrl").ValueKind);
        Assert.Equal(role, data.GetProperty("role").GetString());
        Assert.NotEqual(default, data.GetProperty("joinedAt").GetDateTimeOffset());
        Assert.Equal("local-agent.test", data.GetProperty("emailDomain").GetString());
        Assert.False(
            data.GetProperty("isOutsideAllowedEmailDomains").GetBoolean());
    }

    private sealed class DisappearingMemberListStore : IOrganizationStore
    {
        public int GetByKeyCalls { get; private set; }

        public int ListMembersCalls { get; private set; }

        public Task<OrganizationStorePage<
            OrganizationSummary,
            OrganizationListCursorPosition>> ListAsync(
            UserId actorUserId,
            OrganizationListCursorPosition? after,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OrganizationStorePage<
                OrganizationSummary,
                OrganizationListCursorPosition>([], null));

        public Task<OrganizationOperationResult<OrganizationDetail>>
            GetByKeyAsync(
                UserId actorUserId,
                string organizationKey,
                CancellationToken cancellationToken)
        {
            GetByKeyCalls++;
            Assert.True(OrganizationSlug.TryCreate(
                "stale-precheck",
                out var slug));
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(
                OrganizationOperationResult<OrganizationDetail>.Success(
                    new OrganizationDetail(
                        OrganizationId.New(),
                        "Stale Precheck",
                        slug,
                        now,
                        now,
                        OrganizationRole.Owner,
                        OrganizationPermissionPolicy.GetCapabilities(
                            OrganizationRole.Owner),
                        [])));
        }

        public Task<OrganizationOperationResult<OrganizationDetail>> CreateAsync(
            CreateOrganizationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<OrganizationDetail>> UpdateAsync(
            UpdateOrganizationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<OrganizationDeletion>> DeleteAsync(
            DeleteOrganizationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<ActiveOrganization>> SetActiveAsync(
            SetActiveOrganizationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<
            OrganizationStorePage<
                OrganizationMember,
                OrganizationMemberCursorPosition>>> ListMembersAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
        {
            ListMembersCalls++;
            return Task.FromResult(
                OrganizationOperationResult<
                    OrganizationStorePage<
                        OrganizationMember,
                        OrganizationMemberCursorPosition>>.Failed(
                            OrganizationFailure.NotFound));
        }

        public Task<OrganizationOperationResult<OrganizationMember>>
            AddMemberAsync(
                AddOrganizationMemberCommand command,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<OrganizationMember>>
            UpdateMemberRoleAsync(
                UpdateOrganizationMemberRoleCommand command,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ConcurrencyOrganizationDetailStore : IOrganizationStore
    {
        public int GetByKeyCalls { get; private set; }

        public Task<OrganizationStorePage<
            OrganizationSummary,
            OrganizationListCursorPosition>> ListAsync(
            UserId actorUserId,
            OrganizationListCursorPosition? after,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<OrganizationDetail>>
            GetByKeyAsync(
                UserId actorUserId,
                string organizationKey,
                CancellationToken cancellationToken)
        {
            GetByKeyCalls++;
            return Task.FromResult(
                OrganizationOperationResult<OrganizationDetail>.Failed(
                    OrganizationFailure.ConcurrencyConflict));
        }

        public Task<OrganizationOperationResult<OrganizationDetail>> CreateAsync(
            CreateOrganizationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<OrganizationDetail>> UpdateAsync(
            UpdateOrganizationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<OrganizationDeletion>> DeleteAsync(
            DeleteOrganizationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<ActiveOrganization>> SetActiveAsync(
            SetActiveOrganizationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<
            OrganizationStorePage<
                OrganizationMember,
                OrganizationMemberCursorPosition>>> ListMembersAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<OrganizationMember>>
            AddMemberAsync(
                AddOrganizationMemberCommand command,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrganizationOperationResult<OrganizationMember>>
            UpdateMemberRoleAsync(
                UpdateOrganizationMemberRoleCommand command,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static string CreateChecksumValidOrganizationCursor(
        string normalizedName,
        Guid organizationId)
    {
        var name = Encoding.UTF8.GetBytes(normalizedName);
        var payload = new byte[4 + name.Length + 16];
        payload[0] = 1;
        payload[1] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(2, sizeof(ushort)),
            checked((ushort)name.Length));
        name.CopyTo(payload, 4);
        organizationId.TryWriteBytes(
            payload.AsSpan(4 + name.Length, 16),
            bigEndian: true,
            out _);
        var signed = new byte[payload.Length + 4];
        payload.CopyTo(signed, 0);
        SHA256.HashData(payload)[..4].CopyTo(signed, payload.Length);
        return Convert.ToBase64String(signed)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

internal static class OrganizationEndpointTestSupport
{
    private static int scenarioClientAddress;

    internal static async Task<TestScenario> CreateScenarioAsync(
        HttpClient client,
        string name,
        string email)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/scenario");
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));
        request.Headers.Add(
            "X-Forwarded-For",
            $"198.51.100.{Interlocked.Increment(ref scenarioClientAddress) % 250 + 1}");
        request.Content = JsonContent.Create(new
        {
            name,
            email,
            password = "local-organization-test-password"
        });
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<
            LocalAuthTestClient.ScenarioEnvelope>(
            TestContext.Current.CancellationToken);
        return new TestScenario(
            envelope!.Data.User.Id,
            envelope.Data.Email,
            envelope.Data.Password);
    }

    internal static async Task<HttpResponseMessage> CreateOrganizationAsync(
        HttpClient client,
        string name) =>
        await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/organizations",
            new { name });

    internal static async Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> SendRawWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string body,
        string mediaType)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));
        request.Content = new StringContent(body, Encoding.UTF8, mediaType);
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task<JsonElement> ReadDataAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("data").Clone();
    }

    internal static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            $"urn:template:problem:{code}",
            document.RootElement.GetProperty("type").GetString());
        Assert.False(
            string.IsNullOrWhiteSpace(
                document.RootElement.GetProperty("traceId").GetString()));
    }

    internal static async Task AssertValidationProblemAsync(
        HttpResponseMessage response,
        string field)
    {
        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "validation_failed");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(
            field,
            out _));
    }

    internal static void AssertNoStore(params HttpResponseMessage[] responses) =>
        Assert.All(
            responses,
            response => Assert.Equal(
                "no-store",
                response.Headers.CacheControl?.ToString()));

    internal static async Task<int> CountMembersAsync(
        IServiceProvider services,
        Guid organizationId)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<TemplateDbContext>()
            .OrganizationMembers
            .CountAsync(
                member => member.OrganizationId == organizationId,
                TestContext.Current.CancellationToken);
    }

    internal sealed record TestScenario(Guid UserId, string Email, string Password);
}
