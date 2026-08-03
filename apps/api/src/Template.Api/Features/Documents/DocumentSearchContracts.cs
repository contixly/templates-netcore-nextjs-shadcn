namespace Template.Api.Features.Documents;

internal sealed record DocumentSearchResponse(
    IReadOnlyList<DocumentSearchPageResponse> Pages,
    IReadOnlyList<DocumentSearchHeadingResponse> Headings);

internal sealed record DocumentSearchPageResponse(
    string Type,
    string Title,
    string Description,
    string Href,
    string Group,
    string ParentItem);

internal sealed record DocumentSearchHeadingResponse(
    string Type,
    string Title,
    string Href,
    string PageTitle,
    string Group,
    string ParentItem);

internal sealed class DocumentSearchOptions
{
    public const string SectionName = "Documents";

    public string DefaultLocale { get; init; } = "en";
}
