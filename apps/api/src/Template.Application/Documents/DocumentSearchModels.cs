namespace Template.Application.Documents;

public enum DocumentLocale
{
    En,
    Ru
}

public sealed record DocumentSearchPage(
    string Type,
    string Title,
    string Description,
    string Href,
    string Group,
    string ParentItem,
    int Order,
    string SearchText,
    string TitleText);

public sealed record DocumentSearchHeading(
    string Type,
    string Title,
    string Href,
    string PageTitle,
    string Group,
    string ParentItem,
    int Order,
    string SearchText,
    string TitleText);

public sealed record DocumentSearchLocaleIndex(
    IReadOnlyList<DocumentSearchPage> Pages,
    IReadOnlyList<DocumentSearchHeading> Headings);

public sealed record DocumentSearchRequest(string Query, DocumentLocale Locale);

public sealed record DocumentSearchPageResult(
    string Type,
    string Title,
    string Description,
    string Href,
    string Group,
    string ParentItem);

public sealed record DocumentSearchHeadingResult(
    string Type,
    string Title,
    string Href,
    string PageTitle,
    string Group,
    string ParentItem);

public sealed record DocumentSearchResult(
    IReadOnlyList<DocumentSearchPageResult> Pages,
    IReadOnlyList<DocumentSearchHeadingResult> Headings);
