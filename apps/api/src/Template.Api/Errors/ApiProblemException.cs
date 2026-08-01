namespace Template.Api.Errors;

internal sealed class ApiProblemException(
    int statusCode,
    string code,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception
{
    internal int StatusCode { get; } = statusCode;
    internal string Code { get; } = code;
    internal IReadOnlyDictionary<string, object?> Extensions { get; } =
        extensions ?? new Dictionary<string, object?>();
}
