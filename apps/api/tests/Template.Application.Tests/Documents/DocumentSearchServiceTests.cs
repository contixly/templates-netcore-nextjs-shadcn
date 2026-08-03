using Template.Application.Documents;
using Template.Application.Documents.Ports;

namespace Template.Application.Tests.Documents;

public sealed class DocumentSearchServiceTests
{
    [Fact]
    public void Search_RanksAllMatchKindsBeforeStableNavigationOrder()
    {
        var pages = new[]
        {
            Page("Fuzzy", 0, "unrelated", "giude"),
            Page("Metadata", 1, "unrelated", "metadata guide"),
            Page("Contains", 2, "the guide handbook", "the guide handbook"),
            Page("Prefix", 3, "guide handbook", "guide handbook"),
            Page("Exact later", 9, "guide", "guide"),
            Page("Exact", 4, "guide", "guide"),
            Page("No match", 5, "unrelated", "unrelated"),
        };
        var service = CreateService(pages: pages);

        var result = service.Search(new DocumentSearchRequest("guide", DocumentLocale.En));

        Assert.Equal(
            ["Exact", "Exact later", "Prefix", "Contains", "Metadata", "Fuzzy"],
            result.Pages.Select(page => page.Title));
    }

    [Fact]
    public void Search_RequiresEveryQueryTokenForTheFuzzyScore()
    {
        var service = CreateService(
            pages:
            [
                Page("All tokens", 0, "other", "giude seacrh"),
                Page("One token", 1, "other", "giude reference"),
            ]);

        var result = service.Search(new("guide search", DocumentLocale.En));

        Assert.Equal(["All tokens"], result.Pages.Select(page => page.Title));
    }

    [Fact]
    public void Search_UsesKeyboardVariantsAndTheRequestedLocaleIndex()
    {
        var provider = new StubProvider(
            new([Page("English", 0, "api v1", "api v1")], []),
            new([Page("Русский", 0, "api v1", "api v1")], []));
        var service = new DocumentSearchService(provider);

        var result = service.Search(new("фзш м1", DocumentLocale.Ru));

        Assert.Equal(["Русский"], result.Pages.Select(page => page.Title));
        Assert.Equal(DocumentLocale.Ru, provider.LastLocale);
    }

    [Fact]
    public void Search_MatchesDecomposedQueryToPrecomposedGeneratedIndexText()
    {
        var service = CreateService(
            pages: [Page("Май", 0, "май", "май руководство")]);

        var result = service.Search(new("маи\u0306", DocumentLocale.En));

        Assert.Equal(["Май"], result.Pages.Select(page => page.Title));
    }

    [Fact]
    public void Search_EmptyQueryReturnsOnlyTheFirst32Pages()
    {
        var pages = Enumerable.Range(0, 40)
            .Select(index => Page($"Page {index}", index, $"page {index}", $"page {index}"))
            .ToArray();
        var service = CreateService(
            pages,
            [Heading("Ignored heading", 0, "ignored", "ignored")]);

        var result = service.Search(new(" \t, ", DocumentLocale.En));

        Assert.Equal(32, result.Pages.Count);
        Assert.Equal("Page 0", result.Pages[0].Title);
        Assert.Equal("Page 31", result.Pages[^1].Title);
        Assert.Empty(result.Headings);
    }

    [Fact]
    public void Search_TypedQueryReturnsAtMostEightPagesAndEightHeadings()
    {
        var pages = Enumerable.Range(0, 12)
            .Select(index => Page($"Guide page {index}", index, "guide", "guide"))
            .ToArray();
        var headings = Enumerable.Range(0, 12)
            .Select(index => Heading($"Guide heading {index}", index, "guide", "guide"))
            .ToArray();
        var service = CreateService(pages, headings);

        var result = service.Search(new("guide", DocumentLocale.En));

        Assert.Equal(8, result.Pages.Count);
        Assert.Equal(8, result.Headings.Count);
        Assert.Equal("Guide page 7", result.Pages[^1].Title);
        Assert.Equal("Guide heading 7", result.Headings[^1].Title);
    }

    [Fact]
    public void Search_ProjectsOnlyThePublicResultFields()
    {
        var service = CreateService(
            [Page("Page", 7, "page", "page")],
            [Heading("Heading", 8, "heading", "heading")]);

        var page = Assert.Single(service.Search(new("page", DocumentLocale.En)).Pages);
        var heading = Assert.Single(service.Search(new("heading", DocumentLocale.En)).Headings);

        Assert.Equal(
            ["Description", "Group", "Href", "ParentItem", "Title", "Type"],
            page.GetType().GetProperties().Select(property => property.Name).Order());
        Assert.Equal(
            ["Group", "Href", "PageTitle", "ParentItem", "Title", "Type"],
            heading.GetType().GetProperties().Select(property => property.Name).Order());
    }

    private static DocumentSearchService CreateService(
        IReadOnlyList<DocumentSearchPage>? pages = null,
        IReadOnlyList<DocumentSearchHeading>? headings = null) =>
        new(new StubProvider(new(pages ?? [], headings ?? []), new([], [])));

    private static DocumentSearchPage Page(
        string title,
        int order,
        string titleText,
        string searchText) =>
        new(
            "page",
            title,
            $"{title} description",
            $"/docs/{order}",
            "Group",
            "Parent",
            order,
            searchText,
            titleText);

    private static DocumentSearchHeading Heading(
        string title,
        int order,
        string titleText,
        string searchText) =>
        new(
            "heading",
            title,
            $"/docs/page#{order}",
            "Page",
            "Group",
            "Parent",
            order,
            searchText,
            titleText);

    private sealed class StubProvider(
        DocumentSearchLocaleIndex english,
        DocumentSearchLocaleIndex russian) : IDocumentSearchIndexProvider
    {
        public DocumentLocale? LastLocale { get; private set; }

        public DocumentSearchLocaleIndex Get(DocumentLocale locale)
        {
            LastLocale = locale;
            return locale == DocumentLocale.En ? english : russian;
        }
    }
}
