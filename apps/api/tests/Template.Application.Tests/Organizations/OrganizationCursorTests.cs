using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Template.Application.Organizations;
using Template.Domain.Organizations;

namespace Template.Application.Tests.Organizations;

public sealed class OrganizationCursorTests
{
    private static readonly OrganizationMemberId MembershipId =
        new(Guid.Parse("00000000-0000-0000-0000-000000000010"));
    private static readonly OrganizationMemberId MemberId =
        new(Guid.Parse("00000000-0000-0000-0000-000000000020"));

    [Fact]
    public void Organization_list_cursor_round_trips_membership_utc_ticks_and_uuid()
    {
        var expected = new OrganizationListCursorPosition(
            DateTimeOffset.Parse("2026-07-30T12:34:56.1234567Z"),
            MembershipId);

        var encoded = OrganizationCursor.Encode(expected);

        Assert.DoesNotContain("=", encoded);
        Assert.True(OrganizationCursor.TryDecode(
            encoded,
            out OrganizationListCursorPosition decoded));
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
            new OrganizationListCursorPosition(
                DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
                MembershipId));
        var member = OrganizationCursor.Encode(
            new OrganizationMemberCursorPosition(
                DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
                MemberId));

        Assert.False(OrganizationCursor.TryDecode(
            organization,
            out OrganizationMemberCursorPosition _));
        Assert.False(OrganizationCursor.TryDecode(
            member,
            out OrganizationListCursorPosition _));
    }

    [Fact]
    public void Cursors_reject_noncanonical_tampering_and_extra_bytes()
    {
        var encoded = OrganizationCursor.Encode(
            new OrganizationListCursorPosition(
                DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
                MembershipId));
        var tampered = $"{encoded[..^1]}{(encoded[^1] == 'A' ? 'B' : 'A')}";
        var extraBytes = RewriteAndSign(encoded, payload => [.. payload, (byte)0]);
        var standardAlphabet = CreateTimestampCursor(
            type: 3,
            DateTimeOffset.Parse("2026-07-30T00:00:00Z").UtcTicks,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"))
            .Replace('_', '/');

        Assert.False(OrganizationCursor.TryDecode(
            $"{encoded}=",
            out OrganizationListCursorPosition _));
        Assert.False(OrganizationCursor.TryDecode(
            tampered,
            out OrganizationListCursorPosition _));
        Assert.False(OrganizationCursor.TryDecode(
            extraBytes,
            out OrganizationListCursorPosition _));
        Assert.Contains('/', standardAlphabet);
        Assert.False(OrganizationCursor.TryDecode(
            standardAlphabet,
            out OrganizationListCursorPosition _));
    }

    [Fact]
    public void Organization_list_cursor_rejects_six_byte_legacy_mutable_name_payload()
    {
        const string legacyName = "planet";
        Assert.Equal(6, Encoding.UTF8.GetByteCount(legacyName));
        var encoded = CreateLegacyOrganizationCursor(
            legacyName,
            MembershipId.Value);

        Assert.False(OrganizationCursor.TryDecode(
            encoded,
            out OrganizationListCursorPosition _));
    }

    [Fact]
    public void Cursors_reject_wrong_version_and_unknown_type_even_with_valid_checksum()
    {
        var encoded = OrganizationCursor.Encode(
            new OrganizationListCursorPosition(
                DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
                MembershipId));
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
            out OrganizationListCursorPosition _));
        Assert.False(OrganizationCursor.TryDecode(
            unknownType,
            out OrganizationListCursorPosition _));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public void Cursors_reject_ticks_outside_the_DateTimeOffset_range(byte type)
    {
        var invalidTicks = CreateTimestampCursor(
            type,
            long.MaxValue,
            MembershipId.Value);

        if (type == 3)
        {
            Assert.False(OrganizationCursor.TryDecode(
                invalidTicks,
                out OrganizationListCursorPosition _));
        }
        else
        {
            Assert.False(OrganizationCursor.TryDecode(
                invalidTicks,
                out OrganizationMemberCursorPosition _));
        }
    }

    [Fact]
    public void Organization_list_cursor_requires_a_utc_position_when_encoding()
    {
        var position = new OrganizationListCursorPosition(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(3)),
            MembershipId);

        Assert.Throws<ArgumentException>(() => OrganizationCursor.Encode(position));
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
        return Sign(payload);
    }

    private static string CreateLegacyOrganizationCursor(string name, Guid id)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var payload = new byte[4 + nameBytes.Length + 16];
        payload[0] = 1;
        payload[1] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(2, sizeof(ushort)),
            checked((ushort)nameBytes.Length));
        nameBytes.CopyTo(payload, 4);
        id.TryWriteBytes(
            payload.AsSpan(4 + nameBytes.Length, 16),
            bigEndian: true,
            out _);
        return Sign(payload);
    }

    private static string CreateTimestampCursor(byte type, long ticks, Guid id)
    {
        var payload = new byte[26];
        payload[0] = 1;
        payload[1] = type;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(2, 8), ticks);
        id.TryWriteBytes(payload.AsSpan(10, 16), bigEndian: true, out _);
        return Sign(payload);
    }

    private static string Sign(byte[] payload)
    {
        var signed = new byte[payload.Length + 4];
        payload.CopyTo(signed, 0);
        SHA256.HashData(payload)[..4].CopyTo(signed, payload.Length);
        return Encode(signed);
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
