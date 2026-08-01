using System.Buffers.Binary;
using System.Security.Cryptography;
using Template.Application.Collaboration;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Application.Tests.Collaboration;

public sealed class TeamCursorTests
{
    [Fact]
    public void Team_cursor_round_trips_an_immutable_timestamp_and_identifier()
    {
        var expected = new TeamCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00.1234567Z"),
            new TeamId(Guid.Parse("00000000-0000-0000-0000-000000000001")));

        var encoded = TeamCursor.Encode(expected);

        Assert.DoesNotContain("=", encoded);
        Assert.True(TeamCursor.TryDecode(encoded, out TeamCursorPosition actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Team_cursor_rejects_a_member_cursor()
    {
        var member = TeamCursor.Encode(new TeamMemberCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new TeamMemberId(Guid.Parse("00000000-0000-0000-0000-000000000001"))));

        Assert.False(TeamCursor.TryDecode(member, out TeamCursorPosition _));
    }

    [Fact]
    public void Team_member_and_candidate_cursors_are_distinct_and_round_trip()
    {
        var member = new TeamMemberCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new TeamMemberId(Guid.Parse("00000000-0000-0000-0000-000000000002")));
        var candidate = new TeamCandidateCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new OrganizationMemberId(Guid.Parse("00000000-0000-0000-0000-000000000003")));

        Assert.True(TeamCursor.TryDecode(
            TeamCursor.Encode(member), out TeamMemberCursorPosition decodedMember));
        Assert.True(TeamCursor.TryDecode(
            TeamCursor.Encode(candidate), out TeamCandidateCursorPosition decodedCandidate));
        Assert.Equal(member, decodedMember);
        Assert.Equal(candidate, decodedCandidate);
        Assert.False(TeamCursor.TryDecode(
            TeamCursor.Encode(member), out TeamCandidateCursorPosition _));
        Assert.False(TeamCursor.TryDecode(
            TeamCursor.Encode(candidate), out TeamMemberCursorPosition _));
    }

    [Fact]
    public void Cursors_reject_corrupt_noncanonical_and_extra_bytes()
    {
        var encoded = TeamCursor.Encode(new TeamCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new TeamId(Guid.Parse("00000000-0000-0000-0000-000000000001"))));
        var corrupted = $"{encoded[..^1]}{(encoded[^1] == 'A' ? 'B' : 'A')}";
        var extraBytes = RewriteAndSign(encoded, payload => [.. payload, 0]);
        var standardAlphabet = CreateTimestampCursor(
            type: 4,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z").UtcTicks,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")).Replace('_', '/');

        Assert.False(TeamCursor.TryDecode($"{encoded}=", out TeamCursorPosition _));
        Assert.False(TeamCursor.TryDecode(corrupted, out TeamCursorPosition _));
        Assert.False(TeamCursor.TryDecode(extraBytes, out TeamCursorPosition _));
        Assert.Contains('/', standardAlphabet);
        Assert.False(TeamCursor.TryDecode(standardAlphabet, out TeamCursorPosition _));
    }

    [Fact]
    public void Cursors_require_utc_positions_when_encoding()
    {
        var position = new TeamCursorPosition(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(3)),
            new TeamId(Guid.Parse("00000000-0000-0000-0000-000000000001")));

        Assert.Throws<ArgumentException>(() => TeamCursor.Encode(position));
    }

    [Fact]
    public void Cursors_reject_unknown_versions_types_and_invalid_timestamps()
    {
        var encoded = TeamCursor.Encode(new TeamCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new TeamId(Guid.Parse("00000000-0000-0000-0000-000000000001"))));
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
        var invalidTimestamp = CreateTimestampCursor(
            type: 4,
            long.MaxValue,
            Guid.Parse("00000000-0000-0000-0000-000000000001"));

        Assert.False(TeamCursor.TryDecode(wrongVersion, out TeamCursorPosition _));
        Assert.False(TeamCursor.TryDecode(unknownType, out TeamCursorPosition _));
        Assert.False(TeamCursor.TryDecode(invalidTimestamp, out TeamCursorPosition _));
    }

    private static string RewriteAndSign(string encoded, Func<byte[], byte[]> rewrite) =>
        Sign(rewrite(Decode(encoded)[..^4]));

    private static string CreateTimestampCursor(byte type, long ticks, Guid id)
    {
        var payload = new byte[26];
        payload[0] = 1;
        payload[1] = type;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(2, sizeof(long)), ticks);
        id.TryWriteBytes(payload.AsSpan(10, 16), bigEndian: true, out _);
        return Sign(payload);
    }

    private static string Sign(byte[] payload)
    {
        var signed = new byte[payload.Length + 4];
        payload.CopyTo(signed, 0);
        SHA256.HashData(payload)[..4].CopyTo(signed, payload.Length);
        return Convert.ToBase64String(signed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }
}
