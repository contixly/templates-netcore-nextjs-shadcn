namespace Template.Api.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void E2EHarnessLaunchesTemplateApiInsteadOfHostingTheApi()
    {
        var repositoryRoot = FindRepositoryRoot();
        var harnessDirectory = Path.Combine(
            repositoryRoot,
            "apps",
            "api",
            "tests",
            "Template.E2EHost");
        var project = File.ReadAllText(
            Path.Combine(harnessDirectory, "Template.E2EHost.csproj"));
        var program = File.ReadAllText(
            Path.Combine(harnessDirectory, "Program.cs"));

        Assert.DoesNotContain(
            @"..\..\src\Template.Api\Template.Api.csproj",
            project,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "ApiHost.Build",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "Template.Api.csproj",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "ProcessStartInfo",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "PosixSignal.SIGTERM",
            program,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Template.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the test output directory.");
    }
}
