using System.Buffers.Binary;
using System.Security.Cryptography;
using Template.Application.ApiKeys;
using Template.Domain.ApiKeys;

namespace Template.Application.Tests.ApiKeys;

public sealed class ApiKeyCursorTests
{
    [Fact]
    public void Cursor_round_trips_created_at_and_api_key_id()
    {
        var expected = new ApiKeyCursorPosition(DateTimeOffset.Parse("2026-08-01T00:00:00.1234567Z"), new(Guid.Parse("00000000-0000-0000-0000-000000000001")));
        var encoded = ApiKeyCursor.Encode(expected);

        Assert.True(ApiKeyCursor.TryDecode(encoded, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Cursor_rejects_wrong_version_type_noncanonical_encoding_corruption_and_extra_bytes()
    {
        var encoded = ApiKeyCursor.Encode(new(DateTimeOffset.Parse("2026-08-01T00:00:00Z"), new(Guid.Parse("00000000-0000-0000-0000-000000000001"))));
        var corrupted = $"{encoded[..^1]}{(encoded[^1] == 'A' ? 'B' : 'A')}";
        var wrongVersion = Sign(Rewrite(encoded, payload => { payload[0]++; return payload; }));
        var wrongType = Sign(Rewrite(encoded, payload => { payload[1]++; return payload; }));
        var extra = Sign([.. Rewrite(encoded, payload), 0]);

        Assert.False(ApiKeyCursor.TryDecode(wrongVersion, out _));
        Assert.False(ApiKeyCursor.TryDecode(wrongType, out _));
        Assert.False(ApiKeyCursor.TryDecode($"{encoded}=", out _));
        Assert.False(ApiKeyCursor.TryDecode(corrupted, out _));
        Assert.False(ApiKeyCursor.TryDecode(extra, out _));
    }

    private static byte[] Rewrite(string encoded, Func<byte[], byte[]> rewrite) => rewrite(Decode(encoded)[..^4]);
    private static byte[] payload(byte[] value) => value;
    private static string Sign(byte[] payload)
    {
        var signed = new byte[payload.Length + 4];
        payload.CopyTo(signed, 0);
        SHA256.HashData(payload)[..4].CopyTo(signed, payload.Length);
        return Convert.ToBase64String(signed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
}
