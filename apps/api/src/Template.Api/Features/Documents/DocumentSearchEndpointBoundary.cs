using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
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

    internal static void RequireJsonAccepted(HttpContext http)
    {
        var rawAccept = http.Request.Headers.Accept;
        if (rawAccept.Count == 0)
        {
            return;
        }

        var acceptValues = rawAccept.Select(value => value ?? string.Empty).ToArray();
        if (!MediaTypeHeaderValue.TryParseStrictList(acceptValues, out var accepted) ||
            accepted is null ||
            accepted.Count == 0 ||
            accepted.Any(value => !HasValidQuality(value)))
        {
            throw NotAcceptable();
        }

        var matches = accepted
            .Select(value => new
            {
                Value = value,
                MediaTypeSpecificity = JsonMediaTypeSpecificity(value.MediaType.Value),
                ParameterSpecificity = JsonParameterSpecificity(value)
            })
            .Where(candidate =>
                candidate.MediaTypeSpecificity >= 0 &&
                candidate.ParameterSpecificity >= 0)
            .ToArray();
        if (matches.Length > 0)
        {
            var mostSpecificMediaType = matches.Max(candidate => candidate.MediaTypeSpecificity);
            var mediaTypeMatches = matches
                .Where(candidate => candidate.MediaTypeSpecificity == mostSpecificMediaType)
                .ToArray();
            var mostSpecificParameters = mediaTypeMatches.Max(
                candidate => candidate.ParameterSpecificity);
            if (mediaTypeMatches.Any(candidate =>
                    candidate.ParameterSpecificity == mostSpecificParameters &&
                    (candidate.Value.Quality ?? 1) > 0))
            {
                return;
            }
        }

        throw NotAcceptable();
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

    private static int JsonMediaTypeSpecificity(string? mediaType) => mediaType switch
    {
        var value when string.Equals(value, "application/json", StringComparison.OrdinalIgnoreCase) => 2,
        var value when string.Equals(value, "application/*", StringComparison.OrdinalIgnoreCase) => 1,
        var value when string.Equals(value, "*/*", StringComparison.OrdinalIgnoreCase) => 0,
        _ => -1
    };

    private static int JsonParameterSpecificity(MediaTypeHeaderValue acceptedValue)
    {
        var matchedCharset = false;
        foreach (var parameter in acceptedValue.Parameters)
        {
            if (string.Equals(parameter.Name.Value, "q", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (matchedCharset ||
                !string.Equals(parameter.Name.Value, "charset", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    parameter.Value.Value?.Trim('"'),
                    "utf-8",
                    StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }

            matchedCharset = true;
        }

        return matchedCharset ? 1 : 0;
    }

    private static bool HasValidQuality(MediaTypeHeaderValue acceptedValue)
    {
        var qualityParameterCount = 0;
        foreach (var parameter in acceptedValue.Parameters)
        {
            if (!string.Equals(parameter.Name.Value, "q", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            qualityParameterCount++;
            if (qualityParameterCount > 1 ||
                !IsValidQualityToken(parameter.Value.Value) ||
                acceptedValue.Quality is not double quality ||
                quality is < 0 or > 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidQualityToken(string? value)
    {
        if (value is "0" or "1")
        {
            return true;
        }

        if (value is null ||
            value.Length is < 2 or > 5 ||
            value[1] != '.' ||
            value[0] is not ('0' or '1'))
        {
            return false;
        }

        for (var index = 2; index < value.Length; index++)
        {
            if (!char.IsAsciiDigit(value[index]) ||
                (value[0] == '1' && value[index] != '0'))
            {
                return false;
            }
        }

        return true;
    }

    private static ApiProblemException NotAcceptable() =>
        new(
            StatusCodes.Status406NotAcceptable,
            ApiProblemCodes.NotAcceptable);

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
