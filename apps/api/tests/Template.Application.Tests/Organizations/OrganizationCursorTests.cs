using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Template.Application.Organizations;
using Template.Domain.Organizations;

namespace Template.Application.Tests.Organizations;

public sealed class OrganizationCursorTests
{
    private static readonly OrganizationId OrganizationId =
        new(Guid.Parse("00000000-0000-0000-0000-000000000010"));
    private static readonly OrganizationMemberId MemberId =
        new(Guid.Parse("00000000-0000-0000-0000-000000000020"));

    [Fact]
    public void Organization_cursor_round_trips_unicode_name_and_uuid()
    {
        var expected = new OrganizationCursorPosition("ЖЮ TEAM", OrganizationId);

        var encoded = OrganizationCursor.Encode(expected);

        Assert.DoesNotContain("=", encoded);
        Assert.True(OrganizationCursor.TryDecode(
            encoded,
            out OrganizationCursorPosition decoded));
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void Member_cursor_round_trips_utc_ticks_and_uuid()
    {
        var expected = new OrganizationMemberCursorPosition(
            DateTimeOffset.Parse("2026-07-30T12:34:56.1234567Z"),
            MemberId);

        var encoded = OrganizationCursor.Encode(expected);

        Assert.True(OrganizationCursor.TryDecode(
            encoded,
            out OrganizationMemberCursorPosition decoded));
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void Typed_cursors_cannot_be_decoded_as_the_other_cursor_kind()
    {
        var organization = OrganizationCursor.Encode(
            new OrganizationCursorPosition("ACME", OrganizationId));
        var member = OrganizationCursor.Encode(
            new OrganizationMemberCursorPosition(
                DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
                MemberId));

        Assert.False(OrganizationCursor.TryDecode(
            organization,
            out OrganizationMemberCursorPosition _));
        Assert.False(OrganizationCursor.TryDecode(
            member,
            out OrganizationCursorPosition _));
    }

    [Fact]
    public void Cursors_reject_padding_tampering_and_noncanonical_base64url()
    {
        var encoded = OrganizationCursor.Encode(
            new OrganizationCursorPosition("ACME", OrganizationId));
        var tampered = $"{encoded[..^1]}{(encoded[^1] == 'A' ? 'B' : 'A')}";
        var nonCanonical = ReplaceUnusedBase64Bits(encoded);

        Assert.False(OrganizationCursor.TryDecode(
            $"{encoded}=",
            out OrganizationCursorPosition _));
        Assert.False(OrganizationCursor.TryDecode(
            tampered,
            out OrganizationCursorPosition _));
        Assert.False(OrganizationCursor.TryDecode(
            nonCanonical,
            out OrganizationCursorPosition _));
    }

    [Fact]
    public void Organization_cursor_rejects_invalid_utf8_empty_names_and_extra_bytes()
    {
        var encoded = OrganizationCursor.Encode(
            new OrganizationCursorPosition("ACME", OrganizationId));
        var invalidUtf8 = RewriteAndSign(encoded, payload =>
        {
            payload[2] = 0;
            payload[3] = 1;
            payload[4] = 0xff;
            return payload[..21];
        });
        var emptyName = RewriteAndSign(encoded, payload =>
        {
            payload[2] = 0;
            payload[3] = 0;
            return [.. payload[..4], .. payload[8..24]];
        });
        var extraBytes = RewriteAndSign(encoded, payload => [.. payload, (byte)0]);

        Assert.False(OrganizationCursor.TryDecode(
            invalidUtf8,
            out OrganizationCursorPosition _));
        Assert.False(OrganizationCursor.TryDecode(
            emptyName,
            out OrganizationCursorPosition _));
        Assert.False(OrganizationCursor.TryDecode(
            extraBytes,
            out OrganizationCursorPosition _));
    }

    [Fact]
    public void Cursors_reject_wrong_version_and_unknown_type_even_with_valid_checksum()
    {
        var encoded = OrganizationCursor.Encode(
            new OrganizationCursorPosition("ACME", OrganizationId));
        var wrongVersion = RewriteAndSign(encoded, payload =>
        {
            payload[0]++;
            return payload;
        });
        var unknownType = RewriteAndSign(encoded, payload =>
        {
            payload[1] = 0xff;
            return payload;
        });

        Assert.False(OrganizationCursor.TryDecode(
            wrongVersion,
            out OrganizationCursorPosition _));
        Assert.False(OrganizationCursor.TryDecode(
            unknownType,
            out OrganizationCursorPosition _));
    }

    [Fact]
    public void Member_cursor_rejects_ticks_outside_the_DateTimeOffset_range()
    {
        var encoded = OrganizationCursor.Encode(
            new OrganizationMemberCursorPosition(
                DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
                MemberId));
        var invalidTicks = RewriteAndSign(encoded, payload =>
        {
            BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(2, 8), long.MaxValue);
            return payload;
        });

        Assert.False(OrganizationCursor.TryDecode(
            invalidTicks,
            out OrganizationMemberCursorPosition _));
    }

    [Fact]
    public void Member_cursor_requires_a_utc_position_when_encoding()
    {
        var position = new OrganizationMemberCursorPosition(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(3)),
            MemberId);

        Assert.Throws<ArgumentException>(() => OrganizationCursor.Encode(position));
    }

    private static string RewriteAndSign(
        string encoded,
        Func<byte[], byte[]> rewrite)
    {
        var bytes = Decode(encoded);
        var payload = rewrite(bytes[..^4]);
        var result = new byte[payload.Length + 4];
        payload.CopyTo(result, 0);
        SHA256.HashData(payload)[..4].CopyTo(result, payload.Length);
        return Encode(result);
    }

    private static string ReplaceUnusedBase64Bits(string encoded)
    {
        const string alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var lastIndex = alphabet.IndexOf(encoded[^1]);
        var replacement = alphabet[lastIndex ^ 1];
        var candidate = $"{encoded[..^1]}{replacement}";

        Assert.Equal(Decode(encoded), Decode(candidate));
        return candidate;
    }

    private static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        return Convert.FromBase64String(base64);
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
