namespace Template.Api.Errors;

internal sealed class ApiProblemException(int statusCode, string code) : Exception
{
    internal int StatusCode { get; } = statusCode;
    internal string Code { get; } = code;
}
