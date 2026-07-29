using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace Template.Infrastructure.Authentication;

internal sealed class ProductionDataProtectionCertificate : IDisposable
{
    private X509Certificate2? _certificate;

    private ProductionDataProtectionCertificate(X509Certificate2 certificate)
    {
        _certificate = certificate;
    }

    public X509Certificate2 Certificate =>
        _certificate ?? throw new ObjectDisposedException(
            nameof(ProductionDataProtectionCertificate));

    public static ProductionDataProtectionCertificate Load(
        DataProtectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CertificatePath) ||
            string.IsNullOrWhiteSpace(options.CertificatePassword))
        {
            throw InvalidOptions(
                "DataProtection production certificate path and password are required.");
        }

        X509Certificate2? certificate = null;
        try
        {
            certificate = LoadPkcs12FromFile(
                options.CertificatePath,
                options.CertificatePassword);
            var now = DateTime.UtcNow;
            using var publicKey = certificate.GetRSAPublicKey();
            using var privateKey = certificate.GetRSAPrivateKey();
            if (!certificate.HasPrivateKey ||
                publicKey is null ||
                privateKey is null ||
                certificate.NotBefore.ToUniversalTime() > now ||
                certificate.NotAfter.ToUniversalTime() < now)
            {
                throw InvalidOptions(
                    "DataProtection production certificate must be current and include its private key.");
            }

            return new ProductionDataProtectionCertificate(certificate);
        }
        catch (OptionsValidationException)
        {
            certificate?.Dispose();
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or
            ArgumentException or
            PlatformNotSupportedException)
        {
            certificate?.Dispose();
            throw InvalidOptions(
                "DataProtection production certificate could not be loaded.");
        }
    }

    public void Dispose() =>
        Interlocked.Exchange(ref _certificate, null)?.Dispose();

    private static X509Certificate2 LoadPkcs12FromFile(
        string path,
        string password)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                path,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (PlatformNotSupportedException)
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, password);
        }
    }

    private static OptionsValidationException InvalidOptions(string failure) =>
        new(
            DataProtectionOptions.SectionName,
            typeof(DataProtectionOptions),
            [failure]);
}
