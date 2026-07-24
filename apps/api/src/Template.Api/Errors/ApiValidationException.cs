namespace Template.Api.Errors;

internal sealed class ApiValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
