using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;

namespace Template.Api.Tests.Infrastructure;

internal sealed class TestDataProtectionCertificate : IDisposable
{
    private TestDataProtectionCertificate(string path, string password)
    {
        Path = path;
        Password = password;
    }

    public string Path { get; }
    public string Password { get; }

    public void ConfigureProductionHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("DataProtection:CertificatePath", Path);
        builder.UseSetting("DataProtection:CertificatePassword", Password);
    }

    public static TestDataProtectionCertificate CreateRsa()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Template Data Protection Tests",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DataEncipherment |
                X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
        return Write(certificate);
    }

    public static TestDataProtectionCertificate CreateEcdsa()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=Unsupported Template Data Protection Tests",
            key,
            HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
        return Write(certificate);
    }

    public void Dispose() => File.Delete(Path);

    private static TestDataProtectionCertificate Write(
        X509Certificate2 certificate)
    {
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"template-data-protection-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(
            path,
            certificate.Export(X509ContentType.Pfx, password));
        return new TestDataProtectionCertificate(path, password);
    }
}
