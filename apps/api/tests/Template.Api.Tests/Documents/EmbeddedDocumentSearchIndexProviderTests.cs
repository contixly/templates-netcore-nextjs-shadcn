using Microsoft.Extensions.DependencyInjection;
using Template.Application.Documents;
using Template.Application.Documents.Ports;
using Template.Infrastructure.Documents;

namespace Template.Api.Tests.Documents;

public sealed class EmbeddedDocumentSearchIndexProviderTests
{
    [Fact]
    public void Get_LoadsTheEmbeddedEnglishAndRussianIndexes()
    {
        var provider = new EmbeddedDocumentSearchIndexProvider();

        Assert.Equal(54, provider.Get(DocumentLocale.En).Pages.Count);
        Assert.Contains(
            provider.Get(DocumentLocale.Ru).Pages,
            page => page.Href == "/docs/api/api-v1");
    }

    [Fact]
    public void Parse_RejectsAnUnsupportedSchemaVersion()
    {
        Assert.Throws<InvalidDataException>(() =>
            EmbeddedDocumentSearchIndexProvider.Parse("{\"schemaVersion\":2}"));
    }

    [Fact]
    public void Parse_RejectsDuplicateJsonProperties()
    {
        Assert.Throws<InvalidDataException>(() =>
            EmbeddedDocumentSearchIndexProvider.Parse(
                """
                {
                  "schemaVersion": 1,
                  "schemaVersion": 1,
                  "locales": {
                    "en": { "pages": [], "headings": [] },
                    "ru": { "pages": [], "headings": [] }
                  }
                }
                """));
    }

    [Fact]
    public void Parse_RejectsARequiredPageFieldThatIsMissing()
    {
        Assert.Throws<InvalidDataException>(() =>
            EmbeddedDocumentSearchIndexProvider.Parse(
                """
                {
                  "schemaVersion": 1,
                  "locales": {
                    "en": {
                      "pages": [{
                        "type": "page",
                        "description": "Description",
                        "href": "/docs",
                        "group": "General",
                        "parentItem": "Introduction",
                        "order": 0,
                        "searchText": "description",
                        "titleText": "description"
                      }],
                      "headings": []
                    },
                    "ru": { "pages": [], "headings": [] }
                  }
                }
                """));
    }

    [Fact]
    public void Parse_RejectsNullRequiredValues()
    {
        Assert.Throws<InvalidDataException>(() =>
            EmbeddedDocumentSearchIndexProvider.Parse(
                """
                {
                  "schemaVersion": 1,
                  "locales": {
                    "en": { "pages": null, "headings": [] },
                    "ru": { "pages": [], "headings": [] }
                  }
                }
                """));
    }

    [Fact]
    public void Parse_FreezesTheParsedCollections()
    {
        var index = EmbeddedDocumentSearchIndexProvider.Parse(
            """
            {
              "schemaVersion": 1,
              "locales": {
                "en": {
                  "pages": [{
                    "type": "page",
                    "title": "Documentation",
                    "description": "Description",
                    "href": "/docs",
                    "group": "General",
                    "parentItem": "Introduction",
                    "order": 0,
                    "searchText": "documentation description",
                    "titleText": "documentation"
                  }],
                  "headings": []
                },
                "ru": { "pages": [], "headings": [] }
              }
            }
            """);

        var pages = Assert.IsAssignableFrom<IList<DocumentSearchPage>>(index[DocumentLocale.En].Pages);

        Assert.Throws<NotSupportedException>(() => pages.Clear());
    }

    [Fact]
    public void AddDocumentSearchInfrastructure_RegistersOneSingletonProvider()
    {
        var services = new ServiceCollection();

        services.AddDocumentSearchInfrastructure();
        using var serviceProvider = services.BuildServiceProvider();

        var first = serviceProvider.GetRequiredService<IDocumentSearchIndexProvider>();
        var second = serviceProvider.GetRequiredService<IDocumentSearchIndexProvider>();

        Assert.IsType<EmbeddedDocumentSearchIndexProvider>(first);
        Assert.Same(first, second);
    }
}
