using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Accounts;

public sealed class DataProtectionPersistenceTests(
    PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task SharedDatabaseKeyRingUnprotectsAcrossIndependentHosts()
    {
        var database = await CreateMigratedDatabaseAsync();
        var hostOneRoot = Directory.CreateTempSubdirectory(
            "template-data-protection-host-one-");
        var hostTwoRoot = Directory.CreateTempSubdirectory(
            "template-data-protection-host-two-");
        try
        {
            await using var hostOne = await StartHostAsync(
                "Test",
                database.ConnectionString,
                contentRoot: hostOneRoot.FullName);
            await using var hostTwo = await StartHostAsync(
                "Test",
                database.ConnectionString,
                contentRoot: hostTwoRoot.FullName);

            var protectedPayload = hostOne.Services
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("cross-host")
                .Protect("expected");
            await using (var db = CreateContext(database.ConnectionString))
            {
                Assert.True(await db.DataProtectionKeys.AnyAsync(
                    TestContext.Current.CancellationToken));
            }

            var actual = hostTwo.Services
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("cross-host")
                .Unprotect(protectedPayload);

            Assert.Equal("expected", actual);
        }
        finally
        {
            hostOneRoot.Delete(recursive: true);
            hostTwoRoot.Delete(recursive: true);
            await postgres.DropDatabaseAsync(
                database.DatabaseName,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProductionEncryptedKeyRingSurvivesHostRestart()
    {
        var database = await CreateMigratedDatabaseAsync();
        var certificate = TestDataProtectionCertificate.CreateRsa();
        var hostOneRoot = Directory.CreateTempSubdirectory(
            "template-production-host-one-");
        var hostTwoRoot = Directory.CreateTempSubdirectory(
            "template-production-host-two-");
        try
        {
            string protectedPayload;
            await using (var hostOne = await StartHostAsync(
                Environments.Production,
                database.ConnectionString,
                contentRoot: hostOneRoot.FullName,
                certificatePath: certificate.Path,
                certificatePassword: certificate.Password))
            {
                protectedPayload = hostOne.Services
                    .GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("production-restart")
                    .Protect("expected");
            }

            await using var db = CreateContext(database.ConnectionString);
            var xml = await db.DataProtectionKeys
                .Select(key => key.Xml)
                .SingleAsync(TestContext.Current.CancellationToken);
            var document = XDocument.Parse(xml!);

            Assert.Contains(
                document.Descendants(),
                element => element.Name.LocalName == "EncryptedData");
            Assert.All(
                document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "CipherValue"),
                element => Assert.False(string.IsNullOrWhiteSpace(element.Value)));
            Assert.DoesNotContain(
                document.Descendants(),
                element => element.Name.LocalName == "masterKey");

            await using var hostTwo = await StartHostAsync(
                Environments.Production,
                database.ConnectionString,
                contentRoot: hostTwoRoot.FullName,
                certificatePath: certificate.Path,
                certificatePassword: certificate.Password);
            var actual = hostTwo.Services
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("production-restart")
                .Unprotect(protectedPayload);

            Assert.Equal("expected", actual);
        }
        finally
        {
            hostOneRoot.Delete(recursive: true);
            hostTwoRoot.Delete(recursive: true);
            certificate.Dispose();
            await postgres.DropDatabaseAsync(
                database.DatabaseName,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProductionHostDisposesLoadedCertificate()
    {
        var database = await CreateMigratedDatabaseAsync();
        var certificate = TestDataProtectionCertificate.CreateRsa();
        try
        {
            var host = await StartHostAsync(
                Environments.Production,
                database.ConnectionString,
                certificatePath: certificate.Path,
                certificatePassword: certificate.Password);
            var certificateOwner = host.Services
                .GetRequiredService<ProductionDataProtectionCertificate>();
            var loadedCertificate = certificateOwner.Certificate;

            Assert.NotEqual(IntPtr.Zero, loadedCertificate.Handle);

            await host.DisposeAsync();

            Assert.Equal(IntPtr.Zero, loadedCertificate.Handle);
        }
        finally
        {
            certificate.Dispose();
            await postgres.DropDatabaseAsync(
                database.DatabaseName,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProductionBuildWithoutStartDefersCertificateLoad()
    {
        var missingCertificatePath = Path.Combine(
            Path.GetTempPath(),
            $"template-not-created-{Guid.NewGuid():N}.pfx");

        await using var host = BuildHost(
            Environments.Production,
            UnusedConnectionString,
            certificatePath: missingCertificatePath,
            certificatePassword: "not-a-real-password");

        Assert.False(File.Exists(missingCertificatePath));
    }

    [Fact]
    public void ProductionBuildFailureAfterRegistrationDoesNotLoadCertificate()
    {
        var configuration = CreateProductionConfiguration(
            Path.Combine(
                Path.GetTempPath(),
                $"template-not-created-{Guid.NewGuid():N}.pfx"),
            "not-a-real-password");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthInfrastructure(
            configuration,
            new TestHostEnvironment
            {
                EnvironmentName = Environments.Production
            });
        services.AddSingleton<BuildFailureService>();

        var exception = Assert.ThrowsAny<Exception>(() =>
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true
                }));

        Assert.Contains(
            nameof(IUnregisteredBuildDependency),
            FlattenMessages(exception),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "certificate could not be loaded",
            FlattenMessages(exception),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateProductionRegistrationKeepsSingleCertificateOwner()
    {
        var configuration = CreateProductionConfiguration(
            Path.Combine(
                Path.GetTempPath(),
                $"template-not-created-{Guid.NewGuid():N}.pfx"),
            "not-a-real-password");
        var services = new ServiceCollection();
        services.AddLogging();
        var environment = new TestHostEnvironment
        {
            EnvironmentName = Environments.Production
        };

        services.AddAuthInfrastructure(configuration, environment);
        services.AddAuthInfrastructure(configuration, environment);

        Assert.Single(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(ProductionDataProtectionCertificate));
    }

    [Fact]
    public async Task ProductionWithoutCertificateFailsClosed()
    {
        var database = await CreateMigratedDatabaseAsync();
        try
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var host = await StartHostAsync(
                    Environments.Production,
                    database.ConnectionString);
            });

            Assert.Contains(
                DataProtectionConfigurationName,
                FlattenMessages(exception),
                StringComparison.Ordinal);
        }
        finally
        {
            await postgres.DropDatabaseAsync(
                database.DatabaseName,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProductionWithInvalidCertificateFailsClosed()
    {
        var database = await CreateMigratedDatabaseAsync();
        try
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var host = await StartHostAsync(
                    Environments.Production,
                    database.ConnectionString,
                    certificatePath: Path.Combine(
                        Path.GetTempPath(),
                        $"{Guid.NewGuid():N}.pfx"),
                    certificatePassword: "not-a-real-password");
            });

            Assert.Contains(
                DataProtectionConfigurationName,
                FlattenMessages(exception),
                StringComparison.Ordinal);
        }
        finally
        {
            await postgres.DropDatabaseAsync(
                database.DatabaseName,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProductionWithNonRsaCertificateFailsClosed()
    {
        var database = await CreateMigratedDatabaseAsync();
        var certificate = TestDataProtectionCertificate.CreateEcdsa();
        try
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var host = await StartHostAsync(
                    Environments.Production,
                    database.ConnectionString,
                    certificatePath: certificate.Path,
                    certificatePassword: certificate.Password);
            });

            Assert.Contains(
                DataProtectionConfigurationName,
                FlattenMessages(exception),
                StringComparison.Ordinal);
        }
        finally
        {
            certificate.Dispose();
            await postgres.DropDatabaseAsync(
                database.DatabaseName,
                CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task NonProductionCanUsePersistedKeysWithoutCertificate(
        string environment)
    {
        var database = await CreateMigratedDatabaseAsync();
        try
        {
            await using var host = await StartHostAsync(
                environment,
                database.ConnectionString);

            var protectedPayload = host.Services
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("non-production")
                .Protect("expected");

            Assert.NotEqual("expected", protectedPayload);
        }
        finally
        {
            await postgres.DropDatabaseAsync(
                database.DatabaseName,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task DivergentApplicationNameFailsStartup()
    {
        var database = await CreateMigratedDatabaseAsync();
        try
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var host = await StartHostAsync(
                    "Test",
                    database.ConnectionString,
                    applicationName: "NotTemplate");
            });

            Assert.Contains(
                "DataProtection:ApplicationName",
                FlattenMessages(exception),
                StringComparison.Ordinal);
        }
        finally
        {
            await postgres.DropDatabaseAsync(
                database.DatabaseName,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task LocalJsonOverlayLoadsOnlyInDevelopmentAndEnvironmentWins()
    {
        const string environmentVariable = "Testing__LocalOverlayMarker";
        var contentRoot = Directory.CreateTempSubdirectory(
            "template-local-overlay-");
        var previousEnvironmentValue = Environment.GetEnvironmentVariable(
            environmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                environmentVariable,
                "environment");
            await File.WriteAllTextAsync(
                Path.Combine(contentRoot.FullName, "appsettings.json"),
                JsonSerializer.Serialize(new
                {
                    Testing = new
                    {
                        LocalOverlayMarker = "tracked",
                        LocalOnlyMarker = "tracked"
                    }
                }),
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(contentRoot.FullName, "appsettings.Local.json"),
                JsonSerializer.Serialize(new
                {
                    Testing = new
                    {
                        LocalOverlayMarker = "local",
                        LocalOnlyMarker = "local"
                    }
                }),
                TestContext.Current.CancellationToken);

            await using var development = BuildHost(
                Environments.Development,
                UnusedConnectionString,
                contentRoot.FullName,
                localOverlayCommandLineMarker: "command-line");
            await using var test = BuildHost(
                "Test",
                UnusedConnectionString,
                contentRoot.FullName);

            Assert.Equal(
                "command-line",
                development.Configuration["Testing:LocalOverlayMarker"]);
            Assert.Equal(
                "environment",
                test.Configuration["Testing:LocalOverlayMarker"]);
            Assert.Equal(
                "local",
                development.Configuration["Testing:LocalOnlyMarker"]);
            Assert.Equal(
                "tracked",
                test.Configuration["Testing:LocalOnlyMarker"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                environmentVariable,
                previousEnvironmentValue);
            contentRoot.Delete(recursive: true);
        }
    }

    private const string DataProtectionConfigurationName = "DataProtection";
    private const string UnusedConnectionString =
        "Host=127.0.0.1;Port=1;Database=unused;Username=unused";

    private async Task<(string DatabaseName, string ConnectionString)>
        CreateMigratedDatabaseAsync()
    {
        var database = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        await using var db = CreateContext(database.ConnectionString);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return database;
    }

    private static TemplateDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        TemplateDbContext.Configure(options, connectionString);
        return new TemplateDbContext(options.Options);
    }

    private static IConfiguration CreateProductionConfiguration(
        string certificatePath,
        string certificatePassword) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = UnusedConnectionString,
                ["DataProtection:ApplicationName"] = "Template",
                ["DataProtection:CertificatePath"] = certificatePath,
                ["DataProtection:CertificatePassword"] = certificatePassword
            })
            .Build();

    private static async Task<WebApplication> StartHostAsync(
        string environment,
        string connectionString,
        string? contentRoot = null,
        string? certificatePath = null,
        string? certificatePassword = null,
        string? applicationName = null)
    {
        var host = BuildHost(
            environment,
            connectionString,
            contentRoot,
            certificatePath: certificatePath,
            certificatePassword: certificatePassword,
            applicationName: applicationName);
        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static WebApplication BuildHost(
        string environment,
        string connectionString,
        string? contentRoot = null,
        string? certificatePath = null,
        string? certificatePassword = null,
        string? applicationName = null,
        string? localOverlayCommandLineMarker = null)
    {
        var arguments = new List<string>
        {
            $"--environment={environment}",
            "--urls=http://127.0.0.1:0",
            $"--ConnectionStrings:Postgres={connectionString}"
        };
        if (contentRoot is not null)
        {
            arguments.Add($"--contentRoot={contentRoot}");
        }

        if (certificatePath is not null)
        {
            arguments.Add($"--DataProtection:CertificatePath={certificatePath}");
        }

        if (certificatePassword is not null)
        {
            arguments.Add(
                $"--DataProtection:CertificatePassword={certificatePassword}");
        }

        if (applicationName is not null)
        {
            arguments.Add($"--DataProtection:ApplicationName={applicationName}");
        }

        if (localOverlayCommandLineMarker is not null)
        {
            arguments.Add(
                $"--Testing:LocalOverlayMarker={localOverlayCommandLineMarker}");
        }

        return global::Template.Api.ApiHost.Build([.. arguments]);
    }

    private static string FlattenMessages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(Environment.NewLine, messages);
    }

    private interface IUnregisteredBuildDependency
    {
    }

    private sealed class BuildFailureService
    {
        public BuildFailureService(IUnregisteredBuildDependency dependency)
        {
        }
    }
}
