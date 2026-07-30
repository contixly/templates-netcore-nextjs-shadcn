using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Template.Application.Accounts;

namespace Template.Api.Features.Auth;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ExternalAuthChallengeRequest(
    [property: Required]
    [property: JsonConverter(typeof(ExternalAuthIntentJsonConverter))]
    ExternalAuthIntent? Intent,
    string? ReturnUrl);

internal sealed record ExternalAuthChallengeResponse(string AuthorizationUrl);

internal sealed class ExternalAuthIntentJsonConverter
    : JsonConverter<ExternalAuthIntent?>
{
    public override ExternalAuthIntent? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("External OAuth intent must be a string.");
        }

        return reader.GetString() switch
        {
            "signIn" => ExternalAuthIntent.SignIn,
            "connect" => ExternalAuthIntent.Connect,
            _ => throw new JsonException("External OAuth intent is invalid.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ExternalAuthIntent? value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ExternalAuthIntent.SignIn => "signIn",
            ExternalAuthIntent.Connect => "connect",
            _ => throw new JsonException("External OAuth intent is required.")
        });
    }
}
