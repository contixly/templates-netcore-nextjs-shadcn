using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Template.Api.Errors;

namespace Template.Api.Features.Auth;

internal sealed class ApiJsonRequestReader(IOptions<JsonOptions> jsonOptions)
{
    internal async Task<T> ReadAsync<T>(
        HttpContext context,
        Func<T>? emptyBodyFactory,
        CancellationToken cancellationToken)
        where T : class
    {
        using var reader = new StreamReader(
            context.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        var json = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
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
