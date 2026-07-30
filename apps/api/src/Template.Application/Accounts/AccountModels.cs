using Template.Application.Authentication;
using Template.Domain.Accounts;
using Template.Domain.Authentication;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Template.Application.Accounts;

public sealed record AccountEmail(
    VerifiedEmail Email,
    bool IsPrimary,
    IReadOnlyList<ExternalProvider> Providers);

public sealed record AccountSnapshot(
    AuthUser User,
    VerifiedEmail PrimaryEmail,
    IReadOnlyList<AccountEmail> Emails,
    DateTimeOffset CreatedAt);

public sealed record AccountConnection(
    ExternalProvider Provider,
    bool Configured,
    VerifiedEmail? Email,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastUsedAt);

public sealed record AccountSession(
    SessionId Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    string AuthenticationMethod,
    string? IpAddress,
    string? UserAgent);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record DisconnectSnapshot(
    UserId UserId,
    ExternalProvider Provider,
    VerifiedEmail Email,
    bool EmailIsPrimary,
    int ConfiguredSurvivorCount);

public readonly record struct SessionCursor(DateTimeOffset LastSeenAt, SessionId Id)
{
    private const byte Version = 1;
    private const int PayloadLength = 25;
    private const int ChecksumLength = 8;
    private const int EncodedLength = 44;

    public static string Encode(SessionCursor value)
    {
        Span<byte> bytes = stackalloc byte[PayloadLength + ChecksumLength];
        bytes[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(bytes[1..9], value.LastSeenAt.UtcTicks);
        value.Id.Value.TryWriteBytes(
            bytes[9..PayloadLength],
            bigEndian: true,
            out _);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes[..PayloadLength], hash);
        hash[..ChecksumLength].CopyTo(bytes[PayloadLength..]);

        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string value, out SessionCursor cursor)
    {
        cursor = default;

        if (value is null
            || value.Length != EncodedLength
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_'))
        {
            return false;
        }

        Span<char> base64 = stackalloc char[EncodedLength];
        for (var index = 0; index < value.Length; index++)
        {
            base64[index] = value[index] switch
            {
                '-' => '+',
                '_' => '/',
                var character => character
            };
        }

        Span<byte> bytes = stackalloc byte[PayloadLength + ChecksumLength];
        if (!Convert.TryFromBase64Chars(base64, bytes, out var bytesWritten)
            || bytesWritten != bytes.Length
            || bytes[0] != Version)
        {
            return false;
        }

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes[..PayloadLength], hash);
        if (!CryptographicOperations.FixedTimeEquals(
                hash[..ChecksumLength],
                bytes[PayloadLength..]))
        {
            return false;
        }

        var utcTicks = BinaryPrimitives.ReadInt64BigEndian(bytes[1..9]);
        if (utcTicks < DateTimeOffset.MinValue.UtcTicks
            || utcTicks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        var sessionId = new Guid(bytes[9..PayloadLength], bigEndian: true);
        cursor = new SessionCursor(
            new DateTimeOffset(utcTicks, TimeSpan.Zero),
            new SessionId(sessionId));
        return true;
    }
}

public sealed record AccountDisconnection(ExternalProvider Provider);

public sealed record AccountDeletion(UserId UserId);

public sealed record AccountSessionRevocation(SessionId SessionId);

public enum ExternalAuthIntent
{
    SignIn,
    Connect
}

public sealed record ExternalIdentity(
    ExternalProvider Provider,
    string Subject,
    VerifiedEmail Email,
    string? DisplayName,
    Uri? ImageUrl);

public sealed record ExternalLoginSnapshot(
    UserId UserId,
    ExternalProvider Provider,
    string Subject,
    VerifiedEmail Email,
    DateTimeOffset ConnectedAt,
    DateTimeOffset? LastUsedAt);

public sealed record ExternalAuthentication(
    AuthUser User,
    ExternalProvider Provider,
    bool CreatedUser,
    bool AddedConnection);

public enum AccountFailure
{
    SessionRequired,
    EmailRequired,
    EmailUnverified,
    IdentityConflict,
    EmailConflict,
    ConnectionRequired,
    ConnectionNotFound,
    SessionNotFound,
    CurrentSessionCannotBeRevoked,
    ConfirmationMismatch,
    OrganizationOwnershipTransferRequired,
    ConcurrencyConflict,
    InvalidDisplayName,
    InvalidCursor
}

public sealed record AccountOperationResult<T>(T? Value, AccountFailure? Failure)
    where T : class
{
    public bool Succeeded => Failure is null;
}

public sealed class AccountConcurrencyException : Exception;
