using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Template.Api.Errors;

namespace Template.Api.Features.Auth;

internal sealed class ApiJsonRequestReader(IOptions<JsonOptions> jsonOptions)
{
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    internal async Task<T> ReadAsync<T>(
        HttpContext context,
        Func<T>? emptyBodyFactory,
        CancellationToken cancellationToken)
        where T : class
    {
        using var reader = new StreamReader(
            context.Request.Body,
            StrictUtf8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        string json;
        try
        {
            json = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidRequest);
        }

        if (json.Length == 0)
        {
            if (emptyBodyFactory is not null)
            {
                return emptyBodyFactory();
            }

            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidRequest);
        }

        if (!context.Request.HasJsonContentType())
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidRequest);
        }

        T value;
        try
        {
            value = JsonSerializer.Deserialize<T>(
                json,
                jsonOptions.Value.SerializerOptions) ??
                throw new JsonException("A JSON object is required.");
        }
        catch (JsonException)
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidRequest);
        }

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(
                value,
                new ValidationContext(value),
                validationResults,
                validateAllProperties: true))
        {
            var errors = validationResults
                .SelectMany(result =>
                    result.MemberNames.DefaultIfEmpty("body")
                        .Select(member => (member, result.ErrorMessage)))
                .GroupBy(value => value.member, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(value => value.ErrorMessage ?? "The value is invalid.")
                        .ToArray(),
                    StringComparer.Ordinal);
            throw new ApiValidationException(errors);
        }

        return value;
    }
}
