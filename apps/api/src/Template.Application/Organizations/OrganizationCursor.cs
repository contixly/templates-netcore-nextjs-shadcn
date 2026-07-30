using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Template.Domain.Organizations;

namespace Template.Application.Organizations;

public static class OrganizationCursor
{
    private const byte Version = 1;
    private const byte OrganizationType = 1;
    private const byte MemberType = 2;
    private const int ChecksumLength = 4;
    private const int GuidLength = 16;
    private const int MemberPayloadLength = 2 + sizeof(long) + GuidLength;
    private const int MaximumEncodedLength = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Encode(OrganizationCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (!IsLegitimateNormalizedName(position.NormalizedName))
        {
            throw new ArgumentException(
                "The normalized organization name is invalid.",
                nameof(position));
        }

        var name = StrictUtf8.GetBytes(position.NormalizedName);
        if (name.Length > ushort.MaxValue)
        {
            throw new ArgumentException(
                "The normalized organization name is too long.",
                nameof(position));
        }

        var payload = new byte[4 + name.Length + GuidLength];
        payload[0] = Version;
        payload[1] = OrganizationType;
        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(2, sizeof(ushort)),
            checked((ushort)name.Length));
        name.CopyTo(payload, 4);
        position.Id.Value.TryWriteBytes(
            payload.AsSpan(4 + name.Length, GuidLength),
            bigEndian: true,
            out _);

        return EncodePayload(payload);
    }

    public static string Encode(OrganizationMemberCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (position.JoinedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The member cursor timestamp must use the UTC offset.",
                nameof(position));
        }

        Span<byte> payload = stackalloc byte[MemberPayloadLength];
        payload[0] = Version;
        payload[1] = MemberType;
        BinaryPrimitives.WriteInt64BigEndian(
            payload[2..(2 + sizeof(long))],
            position.JoinedAt.UtcTicks);
        position.Id.Value.TryWriteBytes(
            payload[(2 + sizeof(long))..],
            bigEndian: true,
            out _);

        return EncodePayload(payload);
    }

    public static bool TryDecode(
        string? value,
        out OrganizationCursorPosition position)
    {
        position = default!;

        if (!TryDecodePayload(value, out var payload)
            || payload.Length < 4 + GuidLength
            || payload[0] != Version
            || payload[1] != OrganizationType)
        {
            return false;
        }

        var nameLength = BinaryPrimitives.ReadUInt16BigEndian(
            payload.AsSpan(2, sizeof(ushort)));
        if (nameLength == 0 || payload.Length != 4 + nameLength + GuidLength)
        {
            return false;
        }

        string normalizedName;
        try
        {
            normalizedName = StrictUtf8.GetString(payload, 4, nameLength);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (!IsLegitimateNormalizedName(normalizedName))
        {
            return false;
        }

        var id = new Guid(
            payload.AsSpan(4 + nameLength, GuidLength),
            bigEndian: true);
        position = new OrganizationCursorPosition(
            normalizedName,
            new OrganizationId(id));
        return true;
    }

    public static bool TryDecode(
        string? value,
        out OrganizationMemberCursorPosition position)
    {
        position = default!;

        if (!TryDecodePayload(value, out var payload)
            || payload.Length != MemberPayloadLength
            || payload[0] != Version
            || payload[1] != MemberType)
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

        var id = new Guid(
            payload.AsSpan(2 + sizeof(long), GuidLength),
            bigEndian: true);
        position = new OrganizationMemberCursorPosition(
            new DateTimeOffset(utcTicks, TimeSpan.Zero),
            new OrganizationMemberId(id));
        return true;
    }

    private static string EncodePayload(ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[payload.Length + ChecksumLength];
        payload.CopyTo(bytes);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(payload, hash);
        hash[..ChecksumLength].CopyTo(bytes.AsSpan(payload.Length));

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
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

    private static bool IsLegitimateNormalizedName(string value) =>
        OrganizationNamePolicy.TryNormalize(value, out var normalized) &&
        string.Equals(value, normalized, StringComparison.Ordinal);
}
