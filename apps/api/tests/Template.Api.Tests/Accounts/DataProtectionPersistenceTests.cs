using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Template.Api.Tests.Infrastructure;
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
    public async Task ProductionCertificateEncryptsPersistedKeyXml()
    {
        var database = await CreateMigratedDatabaseAsync();
        var certificate = TestDataProtectionCertificate.CreateRsa();
        try
        {
            await using var host = await StartHostAsync(
                Environments.Production,
                database.ConnectionString,
                certificatePath: certificate.Path,
                certificatePassword: certificate.Password);

            _ = host.Services
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("encrypted-at-rest")
                .Protect("payload");

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
    public async Task LocalJsonOverlayLoadsOnlyInDevelopment()
    {
        var contentRoot = Directory.CreateTempSubdirectory(
            "template-local-overlay-");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(contentRoot.FullName, "appsettings.json"),
                JsonSerializer.Serialize(new
                {
                    Testing = new { LocalOverlayMarker = "tracked" }
                }),
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(contentRoot.FullName, "appsettings.Local.json"),
                JsonSerializer.Serialize(new
                {
                    Testing = new { LocalOverlayMarker = "local" }
                }),
                TestContext.Current.CancellationToken);

            await using var development = BuildHost(
                Environments.Development,
                UnusedConnectionString,
                contentRoot.FullName);
            await using var test = BuildHost(
                "Test",
                UnusedConnectionString,
                contentRoot.FullName);

            Assert.Equal(
                "local",
                development.Configuration["Testing:LocalOverlayMarker"]);
            Assert.Equal(
                "tracked",
                test.Configuration["Testing:LocalOverlayMarker"]);
        }
        finally
        {
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

    private static AuthDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>();
        AuthDbContext.Configure(options, connectionString);
        return new AuthDbContext(options.Options);
    }

    private static async Task<WebApplication> StartHostAsync(
        string environment,
        string connectionString,
        string? contentRoot = null,
        string? certificatePath = null,
        string? certificatePassword = null)
    {
        var host = BuildHost(
            environment,
            connectionString,
            contentRoot,
            certificatePath: certificatePath,
            certificatePassword: certificatePassword);
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
        string? certificatePassword = null)
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
}
