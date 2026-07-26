using Microsoft.AspNetCore.Identity;

namespace Template.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsLocalAutomation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
