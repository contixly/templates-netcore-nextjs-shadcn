namespace Template.Infrastructure.Authentication;

public sealed class DataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string ApplicationName { get; init; } = "Template";
    public string? CertificatePath { get; init; }
    public string? CertificatePassword { get; init; }
}
