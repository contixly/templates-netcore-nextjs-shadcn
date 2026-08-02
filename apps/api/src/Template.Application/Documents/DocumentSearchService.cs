using Template.Application.Documents.Ports;

namespace Template.Application.Documents;

public sealed class DocumentSearchService(IDocumentSearchIndexProvider provider)
{
    private const int EmptyPageLimit = 32;
    private const int TypedPageLimit = 8;
    private const int TypedHeadingLimit = 8;

    public DocumentSearchResult Search(DocumentSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var index = provider.Get(request.Locale);
        var queryVariants = DocumentSearchText.CreateQueryVariants(request.Query)
            .Select(text => new QueryVariant(text, DocumentSearchText.Tokenize(text)))
            .ToArray();

        if (queryVariants.Length == 0)
        {
            return new(
                index.Pages.Take(EmptyPageLimit).Select(ToResult).ToArray(),
                []);
        }

        return new(
            Rank(index.Pages, queryVariants)
                .Take(TypedPageLimit)
                .Select(ToResult)
                .ToArray(),
            Rank(index.Headings, queryVariants)
                .Take(TypedHeadingLimit)
                .Select(ToResult)
                .ToArray());
    }

    private static IEnumerable<T> Rank<T>(
        IEnumerable<T> entries,
        IReadOnlyList<QueryVariant> queryVariants)
        where T : class
    {
        return entries
            .Select(entry => new
            {
                Entry = entry,
                Score = GetBestScore(GetTitleText(entry), GetSearchText(entry), queryVariants),
                Order = GetOrder(entry)
            })
            .Where(ranked => ranked.Score > 0)
            .OrderByDescending(ranked => ranked.Score)
            .ThenBy(ranked => ranked.Order)
            .Select(ranked => ranked.Entry);
    }

    private static int GetBestScore(
        string titleText,
        string searchText,
        IReadOnlyList<QueryVariant> queryVariants) =>
        queryVariants.Max(queryVariant => GetScore(titleText, searchText, queryVariant));

    private static int GetScore(
        string titleText,
        string searchText,
        QueryVariant queryVariant)
    {
        if (titleText == queryVariant.Text)
        {
            return 100;
        }

        if (titleText.StartsWith(queryVariant.Text, StringComparison.Ordinal))
        {
            return 90;
        }

        if (titleText.Contains(queryVariant.Text, StringComparison.Ordinal))
        {
            return 80;
        }

        if (searchText.Contains(queryVariant.Text, StringComparison.Ordinal))
        {
            return 60;
        }

        var candidateTokens = DocumentSearchText.Tokenize(searchText);
        var hasEveryFuzzyQueryToken = queryVariant.Tokens.All(queryToken =>
            candidateTokens.Any(candidateToken => IsFuzzyTokenMatch(queryToken, candidateToken)));

        return hasEveryFuzzyQueryToken ? 40 : 0;
    }

    private static bool IsFuzzyTokenMatch(string queryToken, string candidateToken)
    {
        if (candidateToken == queryToken)
        {
            return true;
        }

        var allowedDistance = DocumentSearchText.GetAllowedTypoDistance(queryToken);

        return allowedDistance > 0 &&
            Math.Abs(candidateToken.Length - queryToken.Length) <= allowedDistance &&
            DocumentSearchText.GetDamerauLevenshteinDistance(queryToken, candidateToken) <=
            allowedDistance;
    }

    private static string GetTitleText<T>(T entry) where T : class => entry switch
    {
        DocumentSearchPage page => page.TitleText,
        DocumentSearchHeading heading => heading.TitleText,
        _ => throw new ArgumentOutOfRangeException(nameof(entry))
    };

    private static string GetSearchText<T>(T entry) where T : class => entry switch
    {
        DocumentSearchPage page => page.SearchText,
        DocumentSearchHeading heading => heading.SearchText,
        _ => throw new ArgumentOutOfRangeException(nameof(entry))
    };

    private static int GetOrder<T>(T entry) where T : class => entry switch
    {
        DocumentSearchPage page => page.Order,
        DocumentSearchHeading heading => heading.Order,
        _ => throw new ArgumentOutOfRangeException(nameof(entry))
    };

    private static DocumentSearchPageResult ToResult(DocumentSearchPage page) =>
        new("page", page.Title, page.Description, page.Href, page.Group, page.ParentItem);

    private static DocumentSearchHeadingResult ToResult(DocumentSearchHeading heading) =>
        new(
            "heading",
            heading.Title,
            heading.Href,
            heading.PageTitle,
            heading.Group,
            heading.ParentItem);

    private sealed record QueryVariant(string Text, IReadOnlyList<string> Tokens);
}
