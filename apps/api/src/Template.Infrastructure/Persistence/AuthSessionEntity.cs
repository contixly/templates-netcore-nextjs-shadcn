using System.Net;

namespace Template.Infrastructure.Persistence;

public sealed class AuthSessionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ActiveOrganizationId { get; set; }
    public required byte[] TicketKeyHash { get; set; }
    public required byte[] ProtectedTicket { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public required string AuthenticationMethod { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
