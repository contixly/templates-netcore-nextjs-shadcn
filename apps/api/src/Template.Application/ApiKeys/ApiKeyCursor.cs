using System.Buffers.Binary;
using System.Security.Cryptography;
using Template.Domain.ApiKeys;

namespace Template.Application.ApiKeys;

public static class ApiKeyCursor
{
    private const byte Version = 1;
    private const byte Type = 9;
    private const int PayloadLength = 2 + sizeof(long) + 16;
    private const int ChecksumLength = 4;
    private const int MaximumEncodedLength = 1024;

    public static string Encode(ApiKeyCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (position.CreatedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The API key cursor timestamp must use the UTC offset.", nameof(position));
        }

        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = Version;
        payload[1] = Type;
        BinaryPrimitives.WriteInt64BigEndian(payload[2..(2 + sizeof(long))], position.CreatedAt.UtcTicks);
        position.Id.Value.TryWriteBytes(payload[(2 + sizeof(long))..], bigEndian: true, out _);
        return EncodePayload(payload);
    }

    public static bool TryDecode(string? value, out ApiKeyCursorPosition position)
    {
        position = default!;
        if (!TryDecodePayload(value, out var payload)
            || payload.Length != PayloadLength
            || payload[0] != Version
            || payload[1] != Type)
        {
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(2, sizeof(long)));
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        position = new(new DateTimeOffset(ticks, TimeSpan.Zero), new(new Guid(payload.AsSpan(2 + sizeof(long), 16), bigEndian: true)));
        return true;
    }

    private static string EncodePayload(ReadOnlySpan<byte> payload)
    {
        var signed = new byte[payload.Length + ChecksumLength];
        payload.CopyTo(signed);
        SHA256.HashData(payload)[..ChecksumLength].CopyTo(signed, payload.Length);
        return Convert.ToBase64String(signed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodePayload(string? value, out byte[] payload)
    {
        payload = [];
        if (string.IsNullOrEmpty(value) || value.Length > MaximumEncodedLength || value.Length % 4 == 1 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
        {
            return false;
        }

        var base64 = value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=');
        var bytes = new byte[base64.Length / 4 * 3];
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written <= ChecksumLength)
        {
            return false;
        }

        Array.Resize(ref bytes, written);
        if (!string.Equals(Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'), value, StringComparison.Ordinal))
        {
            return false;
        }

        var length = bytes.Length - ChecksumLength;
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0, length))[..ChecksumLength], bytes.AsSpan(length)))
        {
            return false;
        }

        payload = bytes[..length];
        return true;
    }
}
