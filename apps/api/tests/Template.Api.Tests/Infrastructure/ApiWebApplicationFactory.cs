using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Template.Api.Endpoints;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Infrastructure;

public sealed class ApiWebApplicationFactory(
    PostgreSqlContainerFixture postgres)
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _databaseName = string.Empty;
    private string _connectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        (_databaseName, _connectionString) = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

    public async Task ResetAuthDataAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.OpenIddictTokens.ExecuteDeleteAsync(cancellationToken);
        await db.UserLogins.ExecuteDeleteAsync(cancellationToken);
        await db.UserEmails.ExecuteDeleteAsync(cancellationToken);
        await db.Sessions.ExecuteDeleteAsync(cancellationToken);
        await db.Users.ExecuteDeleteAsync(cancellationToken);
    }

    public Task<(string DatabaseName, string ConnectionString)>
        CreateUnmigratedDatabaseAsync(CancellationToken cancellationToken) =>
        postgres.CreateDatabaseAsync(cancellationToken);

    public Task DropDatabaseAsync(
        string databaseName,
        CancellationToken cancellationToken) =>
        postgres.DropDatabaseAsync(databaseName, cancellationToken);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _connectionString,
                ["LocalAutomationAuth:Enabled"] = "true",
                ["LocalAutomationAuth:CreateRateLimitPerMinute"] = "20",
                ["LocalAutomationAuth:SignInRateLimitPerFiveMinutes"] = "10"
            }));
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter<CapturedLogProvider>(level => level >= LogLevel.Debug);
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthorization(options =>
            {
                options.AddPolicy(
                    TestEndpointModule.ForbiddenPolicy,
                    policy => policy.RequireClaim("test.permission", "granted"));
            });
            services.AddSingleton<IEndpointModule, TestEndpointModule>();
            services.AddSingleton<CapturedLogProvider>();
            services.AddSingleton<ILoggerProvider>(
                provider => provider.GetRequiredService<CapturedLogProvider>());
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_databaseName.Length > 0)
        {
            await postgres.DropDatabaseAsync(
                _databaseName,
                CancellationToken.None);
        }
    }
}
