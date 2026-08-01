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

    [Fact]
    public void Persistence_context_is_named_for_the_whole_template()
    {
        var names = typeof(Template.Infrastructure.Persistence.TemplateDbContext)
            .Assembly.GetTypes()
            .Select(type => type.Name)
            .ToArray();

        Assert.Contains("TemplateDbContext", names);
        Assert.DoesNotContain("AuthDbContext", names);
        Assert.Contains("EfApplicationUnitOfWork", names);
    }

    [Fact]
    public void OpenApi_export_invalidates_its_cache_when_the_canonical_output_is_missing()
    {
        var repositoryRoot = FindRepositoryRoot();
        var project = System.Xml.Linq.XDocument.Load(
            Path.Combine(
                repositoryRoot,
                "apps",
                "api",
                "src",
                "Template.Api",
                "Template.Api.csproj"));
        var target = Assert.Single(
            project.Descendants("Target"),
            element => string.Equals(
                (string?)element.Attribute("Name"),
                "InvalidateMissingOpenApiDocumentCache",
                StringComparison.Ordinal));

        Assert.Equal(
            "GenerateOpenApiDocuments",
            (string?)target.Attribute("BeforeTargets"));
        Assert.Contains(
            "!Exists('$(OpenApiDocumentsDirectory)/v1.json')",
            (string?)target.Attribute("Condition"),
            StringComparison.Ordinal);
        var delete = Assert.Single(target.Elements("Delete"));
        Assert.Equal(
            "$(_OpenApiDocumentsCache)",
            (string?)delete.Attribute("Files"));
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
