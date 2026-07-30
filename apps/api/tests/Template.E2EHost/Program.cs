using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Template.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
using var terminateRegistration = OperatingSystem.IsWindows()
    ? null
    : PosixSignalRegistration.Create(
        PosixSignal.SIGTERM,
        context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        });

await using var postgres = new PostgreSqlBuilder("postgres:18.4")
    .WithDatabase("template_e2e")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();
await postgres.StartAsync(shutdown.Token);

var connectionString = postgres.GetConnectionString();
var databaseOptions = new DbContextOptionsBuilder<TemplateDbContext>();
TemplateDbContext.Configure(databaseOptions, connectionString);
await using (var database = new TemplateDbContext(databaseOptions.Options))
{
    await database.Database.MigrateAsync(shutdown.Token);
}

var repositoryRoot = FindRepositoryRoot();
var apiProject = Path.Combine(
    repositoryRoot,
    "apps",
    "api",
    "src",
    "Template.Api",
    "Template.Api.csproj");
using var api = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        WorkingDirectory = repositoryRoot,
        UseShellExecute = false
    }
};
api.StartInfo.ArgumentList.Add("run");
api.StartInfo.ArgumentList.Add("--no-launch-profile");
api.StartInfo.ArgumentList.Add("--project");
api.StartInfo.ArgumentList.Add(apiProject);
api.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Test";
api.StartInfo.Environment["ConnectionStrings__Postgres"] = connectionString;
api.StartInfo.Environment["LocalAutomationAuth__Enabled"] = "true";
api.StartInfo.Environment["Testing__AssumeHttpsBoundary"] = "true";
CopyExternalAuthenticationEnvironment(api.StartInfo.Environment);

if (!api.Start())
{
    throw new InvalidOperationException("Template.Api did not start.");
}

try
{
    await api.WaitForExitAsync(shutdown.Token);
    Environment.ExitCode = api.ExitCode;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    // Normal Playwright or terminal shutdown.
}
finally
{
    if (!api.HasExited)
    {
        api.Kill(entireProcessTree: true);
        await api.WaitForExitAsync();
    }
}

static string FindRepositoryRoot()
{
    foreach (var startingPath in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(startingPath);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Template.sln")))
            {
                return directory.FullName;
            }
        }
    }

    throw new DirectoryNotFoundException(
        "Could not locate Template.sln for the E2E API process.");
}

static void CopyExternalAuthenticationEnvironment(
    IDictionary<string, string?> target)
{
    const string section = "ExternalAuthentication";
    const string publicOrigin = $"{section}__PublicOrigin";
    var providerNames = new[] { "Google", "GitHub", "GitLab", "Vk", "Yandex" };

    foreach (var name in target.Keys
                 .Where(name => name.StartsWith(
                     $"{section}__",
                     StringComparison.OrdinalIgnoreCase))
                 .ToArray())
    {
        target.Remove(name);
    }

    CopyConfiguredValue(publicOrigin);
    foreach (var provider in providerNames)
    {
        var prefix = $"{section}__Providers__{provider}";
        var clientIdName = $"{prefix}__ClientId";
        var clientSecretName = $"{prefix}__ClientSecret";
        var clientId = ReadConfiguredValue(clientIdName);
        var clientSecret = ReadConfiguredValue(clientSecretName);
        if (clientId is not null && clientSecret is not null)
        {
            target[clientIdName] = clientId;
            target[clientSecretName] = clientSecret;
        }
    }

    void CopyConfiguredValue(string name)
    {
        var value = ReadConfiguredValue(name);
        if (value is not null)
        {
            target[name] = value;
        }
    }

    static string? ReadConfiguredValue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value)
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
                ? value
                : null;
    }
}
