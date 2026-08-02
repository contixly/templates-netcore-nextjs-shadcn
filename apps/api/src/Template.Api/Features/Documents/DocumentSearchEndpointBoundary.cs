using Microsoft.Extensions.Options;
using Template.Api.Errors;
using Template.Application.Documents;

namespace Template.Api.Features.Documents;

internal static class DocumentSearchEndpointBoundary
{
    internal const int MaximumQueryLength = 120;

    internal static DocumentSearchRequest Request(
        HttpContext http,
        string? q,
        string? locale,
        IOptions<DocumentSearchOptions> options)
    {
        RequireExactQuery(http, "q", "locale");

        var query = SingleQueryValue(http, "q", q)?.Trim() ?? string.Empty;
        if (query.Length > MaximumQueryLength)
        {
            throw Validation(
                "q",
                $"The field q must be at most {MaximumQueryLength} characters after trimming.");
        }

        var requestedLocale = SingleQueryValue(http, "locale", locale);
        return new DocumentSearchRequest(
            query,
            requestedLocale is null
                ? ConfiguredDefaultLocale(options.Value.DefaultLocale)
                : ExplicitLocale(requestedLocale));
    }

    internal static DocumentSearchResponse Response(DocumentSearchResult result) =>
        new(
            result.Pages.Select(page => new DocumentSearchPageResponse(
                page.Type,
                page.Title,
                page.Description,
                page.Href,
                page.Group,
                page.ParentItem)).ToArray(),
            result.Headings.Select(heading => new DocumentSearchHeadingResponse(
                heading.Type,
                heading.Title,
                heading.Href,
                heading.PageTitle,
                heading.Group,
                heading.ParentItem)).ToArray());

    internal static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.OnStarting(static state =>
        {
            ((HttpContext)state).Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        }, http);
    }

    private static DocumentLocale ConfiguredDefaultLocale(string? value) =>
        value switch
        {
            "ru" => DocumentLocale.Ru,
            _ => DocumentLocale.En
        };

    private static DocumentLocale ExplicitLocale(string value) =>
        value switch
        {
            "en" => DocumentLocale.En,
            "ru" => DocumentLocale.Ru,
            _ => throw Validation(
                "locale",
                "The field locale must be either en or ru.")
        };

    private static void RequireExactQuery(HttpContext http, params string[] allowedNames)
    {
        var allowed = allowedNames.ToHashSet(StringComparer.Ordinal);
        if (http.Request.Query.Keys.Any(key => !allowed.Contains(key)))
        {
            throw Validation("query", "The query contains an unsupported field.");
        }
    }

    private static string? SingleQueryValue(
        HttpContext http,
        string name,
        string? boundValue)
    {
        if (!http.Request.Query.TryGetValue(name, out var values))
        {
            return null;
        }

        if (values.Count != 1 ||
            !string.Equals(values[0], boundValue, StringComparison.Ordinal))
        {
            throw Validation(name, $"The field {name} must be supplied at most once.");
        }

        return values[0];
    }

    private static ApiValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
