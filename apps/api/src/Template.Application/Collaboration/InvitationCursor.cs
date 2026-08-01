using System.Buffers.Binary;
using System.Security.Cryptography;
using Template.Domain.Collaboration;

namespace Template.Application.Collaboration;

public static class InvitationCursor
{
    private const byte Version = 1;
    private const byte OrganizationInvitationListType = 7;
    private const byte AccountInvitationListType = 8;
    private const int ChecksumLength = 4;
    private const int GuidLength = 16;
    private const int OrganizationPositionPayloadLength = 2 + sizeof(long) + GuidLength;
    private const int AccountPositionPayloadLength = 2 + (sizeof(long) * 2) + GuidLength;
    private const int MaximumEncodedLength = 1024;

    public static string Encode(OrganizationInvitationCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        ValidateUtc(position.CreatedAt, "The organization invitation cursor timestamp must use the UTC offset.", nameof(position));

        Span<byte> payload = stackalloc byte[OrganizationPositionPayloadLength];
        payload[0] = Version;
        payload[1] = OrganizationInvitationListType;
        BinaryPrimitives.WriteInt64BigEndian(payload[2..(2 + sizeof(long))], position.CreatedAt.UtcTicks);
        position.Id.Value.TryWriteBytes(payload[(2 + sizeof(long))..], bigEndian: true, out _);
        return EncodePayload(payload);
    }

    public static string Encode(AccountInvitationCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        ValidateUtc(position.ExpiresAt, "The account invitation expiry timestamp must use the UTC offset.", nameof(position));
        ValidateUtc(position.CreatedAt, "The account invitation creation timestamp must use the UTC offset.", nameof(position));

        Span<byte> payload = stackalloc byte[AccountPositionPayloadLength];
        payload[0] = Version;
        payload[1] = AccountInvitationListType;
        BinaryPrimitives.WriteInt64BigEndian(payload[2..(2 + sizeof(long))], position.ExpiresAt.UtcTicks);
        BinaryPrimitives.WriteInt64BigEndian(payload[(2 + sizeof(long))..(2 + (sizeof(long) * 2))], position.CreatedAt.UtcTicks);
        position.Id.Value.TryWriteBytes(payload[(2 + (sizeof(long) * 2))..], bigEndian: true, out _);
        return EncodePayload(payload);
    }

    public static bool TryDecode(string? value, out OrganizationInvitationCursorPosition position)
    {
        position = default!;
        if (!TryDecodePayload(value, OrganizationPositionPayloadLength, OrganizationInvitationListType, out var payload)
            || !TryReadUtcTimestamp(payload.AsSpan(2, sizeof(long)), out var createdAt))
        {
            return false;
        }

        position = new OrganizationInvitationCursorPosition(
            createdAt,
            new InvitationId(new Guid(payload.AsSpan(2 + sizeof(long), GuidLength), bigEndian: true)));
        return true;
    }

    public static bool TryDecode(string? value, out AccountInvitationCursorPosition position)
    {
        position = default!;
        if (!TryDecodePayload(value, AccountPositionPayloadLength, AccountInvitationListType, out var payload)
            || !TryReadUtcTimestamp(payload.AsSpan(2, sizeof(long)), out var expiresAt)
            || !TryReadUtcTimestamp(payload.AsSpan(2 + sizeof(long), sizeof(long)), out var createdAt))
        {
            return false;
        }

        position = new AccountInvitationCursorPosition(
            expiresAt,
            createdAt,
            new InvitationId(new Guid(payload.AsSpan(2 + (sizeof(long) * 2), GuidLength), bigEndian: true)));
        return true;
    }

    private static bool TryDecodePayload(string? value, int expectedLength, byte expectedType, out byte[] payload)
    {
        payload = [];
        if (!TryDecodePayload(value, out var decoded)
            || decoded.Length != expectedLength
            || decoded[0] != Version
            || decoded[1] != expectedType)
        {
            return false;
        }

        payload = decoded;
        return true;
    }

    private static bool TryReadUtcTimestamp(ReadOnlySpan<byte> bytes, out DateTimeOffset timestamp)
    {
        timestamp = default;
        var utcTicks = BinaryPrimitives.ReadInt64BigEndian(bytes);
        if (utcTicks < DateTimeOffset.MinValue.UtcTicks || utcTicks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        timestamp = new DateTimeOffset(utcTicks, TimeSpan.Zero);
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
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
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
        if (!Convert.TryFromBase64Chars(base64, bytes, out var bytesWritten)
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
        if (!CryptographicOperations.FixedTimeEquals(hash[..ChecksumLength], bytes.AsSpan(payloadLength, ChecksumLength)))
        {
            return false;
        }

        payload = bytes[..payloadLength];
        return true;
    }

    private static string EncodeBytes(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void ValidateUtc(DateTimeOffset timestamp, string message, string parameterName)
    {
        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(message, parameterName);
        }
    }
}
