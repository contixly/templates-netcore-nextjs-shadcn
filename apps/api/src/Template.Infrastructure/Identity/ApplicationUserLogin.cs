using Microsoft.AspNetCore.Identity;

namespace Template.Infrastructure.Identity;

public sealed class ApplicationUserLogin : IdentityUserLogin<Guid>
{
    public Guid VerifiedEmailId { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
