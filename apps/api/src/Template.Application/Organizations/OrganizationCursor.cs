using System.Buffers.Binary;
using System.Security.Cryptography;
using Template.Domain.Organizations;

namespace Template.Application.Organizations;

public static class OrganizationCursor
{
    private const byte Version = 1;
    private const byte OrganizationListType = 3;
    private const byte MemberListType = 2;
    private const int ChecksumLength = 4;
    private const int GuidLength = 16;
    private const int PositionPayloadLength = 2 + sizeof(long) + GuidLength;
    private const int MaximumEncodedLength = 1024;

    public static string Encode(OrganizationListCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        ValidateUtc(
            position.MembershipJoinedAt,
            "The organization-list cursor timestamp must use the UTC offset.",
            nameof(position));
        return EncodePosition(
            OrganizationListType,
            position.MembershipJoinedAt,
            position.MembershipId);
    }

    public static string Encode(OrganizationMemberCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        ValidateUtc(
            position.JoinedAt,
            "The member cursor timestamp must use the UTC offset.",
            nameof(position));
        return EncodePosition(MemberListType, position.JoinedAt, position.Id);
    }

    public static bool TryDecode(
        string? value,
        out OrganizationListCursorPosition position)
    {
        position = default!;
        if (!TryDecodePosition(
                value,
                OrganizationListType,
                out var joinedAt,
                out var membershipId))
        {
            return false;
        }

        position = new OrganizationListCursorPosition(joinedAt, membershipId);
        return true;
    }

    public static bool TryDecode(
        string? value,
        out OrganizationMemberCursorPosition position)
    {
        position = default!;
        if (!TryDecodePosition(
                value,
                MemberListType,
                out var joinedAt,
                out var memberId))
        {
            return false;
        }

        position = new OrganizationMemberCursorPosition(joinedAt, memberId);
        return true;
    }

    private static string EncodePosition(
        byte type,
        DateTimeOffset joinedAt,
        OrganizationMemberId id)
    {
        Span<byte> payload = stackalloc byte[PositionPayloadLength];
        payload[0] = Version;
        payload[1] = type;
        BinaryPrimitives.WriteInt64BigEndian(
            payload[2..(2 + sizeof(long))],
            joinedAt.UtcTicks);
        id.Value.TryWriteBytes(
            payload[(2 + sizeof(long))..],
            bigEndian: true,
            out _);
        return EncodePayload(payload);
    }

    private static bool TryDecodePosition(
        string? value,
        byte type,
        out DateTimeOffset joinedAt,
        out OrganizationMemberId id)
    {
        joinedAt = default;
        id = default;
        if (!TryDecodePayload(value, out var payload)
            || payload.Length != PositionPayloadLength
            || payload[0] != Version
            || payload[1] != type)
        {
            return false;
        }

        var utcTicks = BinaryPrimitives.ReadInt64BigEndian(
            payload.AsSpan(2, sizeof(long)));
        if (utcTicks < DateTimeOffset.MinValue.UtcTicks
            || utcTicks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        joinedAt = new DateTimeOffset(utcTicks, TimeSpan.Zero);
        id = new OrganizationMemberId(new Guid(
            payload.AsSpan(2 + sizeof(long), GuidLength),
            bigEndian: true));
        return true;
    }

    private static string EncodePayload(ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[payload.Length + ChecksumLength];
        payload.CopyTo(bytes);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(payload, hash);
        hash[..ChecksumLength].CopyTo(bytes.AsSpan(payload.Length));

        return EncodeBytes(bytes);
    }

    private static bool TryDecodePayload(string? value, out byte[] payload)
    {
        payload = [];

        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumEncodedLength
            || value.Length % 4 == 1
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_'))
        {
            return false;
        }

        var base64Length = (value.Length + 3) / 4 * 4;
        var base64 = new char[base64Length];
        for (var index = 0; index < value.Length; index++)
        {
            base64[index] = value[index] switch
            {
                '-' => '+',
                '_' => '/',
                var character => character
            };
        }

        for (var index = value.Length; index < base64.Length; index++)
        {
            base64[index] = '=';
        }

        var bytes = new byte[base64Length / 4 * 3];
        if (!Convert.TryFromBase64Chars(
                base64,
                bytes,
                out var bytesWritten)
            || bytesWritten <= ChecksumLength)
        {
            return false;
        }

        Array.Resize(ref bytes, bytesWritten);
        if (!string.Equals(EncodeBytes(bytes), value, StringComparison.Ordinal))
        {
            return false;
        }

        var payloadLength = bytes.Length - ChecksumLength;
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes.AsSpan(0, payloadLength), hash);
        if (!CryptographicOperations.FixedTimeEquals(
                hash[..ChecksumLength],
                bytes.AsSpan(payloadLength, ChecksumLength)))
        {
            return false;
        }

        payload = bytes[..payloadLength];
        return true;
    }

    private static string EncodeBytes(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static void ValidateUtc(
        DateTimeOffset timestamp,
        string message,
        string parameterName)
    {
        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(message, parameterName);
        }
    }
}
