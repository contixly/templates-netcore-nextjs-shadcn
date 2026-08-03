using System.Collections.ObjectModel;
using System.Text.Json;
using Template.Application.Documents;
using Template.Application.Documents.Ports;

namespace Template.Infrastructure.Documents;

public sealed class EmbeddedDocumentSearchIndexProvider : IDocumentSearchIndexProvider
{
    private const string ResourceName = "Template.Documents.SearchIndex.v1.json";

    private static readonly Lazy<IReadOnlyDictionary<DocumentLocale, DocumentSearchLocaleIndex>>
        EmbeddedIndex = new(LoadEmbeddedIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    public DocumentSearchLocaleIndex Get(DocumentLocale locale) => locale switch
    {
        DocumentLocale.En => EmbeddedIndex.Value[DocumentLocale.En],
        DocumentLocale.Ru => EmbeddedIndex.Value[DocumentLocale.Ru],
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unsupported document locale.")
    };

    internal static IReadOnlyDictionary<DocumentLocale, DocumentSearchLocaleIndex> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowDuplicateProperties = false,
                    CommentHandling = JsonCommentHandling.Disallow
                });

            var root = document.RootElement;
            RequireExactProperties(root, "schemaVersion", "locales");
            if (RequireInt32(root, "schemaVersion") != 1)
            {
                throw new InvalidDataException("Document search index schemaVersion must be 1.");
            }

            var locales = RequireObject(root, "locales");
            RequireExactProperties(locales, "en", "ru");

            return new ReadOnlyDictionary<DocumentLocale, DocumentSearchLocaleIndex>(
                new Dictionary<DocumentLocale, DocumentSearchLocaleIndex>
                {
                    [DocumentLocale.En] = ParseLocale(RequireObject(locales, "en")),
                    [DocumentLocale.Ru] = ParseLocale(RequireObject(locales, "ru"))
                });
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Document search index JSON is invalid.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("Document search index JSON is invalid.", exception);
        }
    }

    private static IReadOnlyDictionary<DocumentLocale, DocumentSearchLocaleIndex> LoadEmbeddedIndex()
    {
        var assembly = typeof(EmbeddedDocumentSearchIndexProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException(
                $"Embedded document search index '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    private static DocumentSearchLocaleIndex ParseLocale(JsonElement locale)
    {
        RequireExactProperties(locale, "pages", "headings");

        return new DocumentSearchLocaleIndex(
            Freeze(ParseArray(RequireArray(locale, "pages"), ParsePage)),
            Freeze(ParseArray(RequireArray(locale, "headings"), ParseHeading)));
    }

    private static DocumentSearchPage ParsePage(JsonElement page)
    {
        RequireExactProperties(
            page,
            "type",
            "title",
            "description",
            "href",
            "group",
            "parentItem",
            "order",
            "searchText",
            "titleText");

        var type = RequireString(page, "type");
        if (!string.Equals(type, "page", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Document search page type must be 'page'.");
        }

        return new DocumentSearchPage(
            type,
            RequireString(page, "title"),
            RequireString(page, "description"),
            RequireString(page, "href"),
            RequireString(page, "group"),
            RequireString(page, "parentItem"),
            RequireInt32(page, "order"),
            RequireString(page, "searchText"),
            RequireString(page, "titleText"));
    }

    private static DocumentSearchHeading ParseHeading(JsonElement heading)
    {
        RequireExactProperties(
            heading,
            "type",
            "title",
            "href",
            "pageTitle",
            "group",
            "parentItem",
            "order",
            "searchText",
            "titleText");

        var type = RequireString(heading, "type");
        if (!string.Equals(type, "heading", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Document search heading type must be 'heading'.");
        }

        return new DocumentSearchHeading(
            type,
            RequireString(heading, "title"),
            RequireString(heading, "href"),
            RequireString(heading, "pageTitle"),
            RequireString(heading, "group"),
            RequireString(heading, "parentItem"),
            RequireInt32(heading, "order"),
            RequireString(heading, "searchText"),
            RequireString(heading, "titleText"));
    }

    private static IEnumerable<T> ParseArray<T>(
        JsonElement array,
        Func<JsonElement, T> parse)
    {
        foreach (var item in array.EnumerateArray())
        {
            yield return parse(item);
        }
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());

    private static JsonElement RequireObject(JsonElement parent, string propertyName)
    {
        var value = RequireProperty(parent, propertyName);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Document search index property '{propertyName}' must be an object.");
        }

        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string propertyName)
    {
        var value = RequireProperty(parent, propertyName);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Document search index property '{propertyName}' must be an array.");
        }

        return value;
    }

    private static string RequireString(JsonElement parent, string propertyName)
    {
        var value = RequireProperty(parent, propertyName);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"Document search index property '{propertyName}' must be a string.");
        }

        return value.GetString()
            ?? throw new InvalidDataException(
                $"Document search index property '{propertyName}' cannot be null.");
    }

    private static int RequireInt32(JsonElement parent, string propertyName)
    {
        var value = RequireProperty(parent, propertyName);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new InvalidDataException(
                $"Document search index property '{propertyName}' must be an Int32.");
        }

        return number;
    }

    private static JsonElement RequireProperty(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            throw new InvalidDataException(
                $"Document search index property '{propertyName}' is required.");
        }

        return value;
    }

    private static void RequireExactProperties(JsonElement value, params string[] expectedProperties)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Document search index value must be an object.");
        }

        var expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
        var actualCount = 0;
        foreach (var property in value.EnumerateObject())
        {
            actualCount++;
            if (!expected.Remove(property.Name))
            {
                throw new InvalidDataException(
                    $"Document search index property '{property.Name}' is not allowed.");
            }
        }

        if (actualCount != expectedProperties.Length || expected.Count != 0)
        {
            throw new InvalidDataException("Document search index object has missing properties.");
        }
    }
}
