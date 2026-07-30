namespace Template.Infrastructure.Authentication;

public sealed class DataProtectionOptions
{
    public const string SectionName = "DataProtection";
    public const string RequiredApplicationName = "Template";

    public string ApplicationName { get; init; } = RequiredApplicationName;
    public string? CertificatePath { get; init; }
    public string? CertificatePassword { get; init; }
}
