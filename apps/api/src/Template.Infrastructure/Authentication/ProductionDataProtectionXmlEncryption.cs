using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Template.Infrastructure.Authentication;

internal sealed class ProductionDataProtectionKeyManagementSetup(
    ProductionDataProtectionCertificate certificate,
    ILoggerFactory loggerFactory)
    : IConfigureOptions<KeyManagementOptions>
{
    public void Configure(KeyManagementOptions options) =>
        options.XmlEncryptor = new ProductionCertificateXmlEncryptor(
            certificate.Certificate,
            loggerFactory);
}

internal sealed class ProductionCertificateXmlEncryptor(
    X509Certificate2 certificate,
    ILoggerFactory loggerFactory)
    : IXmlEncryptor
{
    private readonly CertificateXmlEncryptor _inner =
        new(certificate, loggerFactory);

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var encrypted = _inner.Encrypt(plaintextElement);
        return new EncryptedXmlInfo(
            encrypted.EncryptedElement,
            typeof(ProductionCertificateXmlDecryptor));
    }
}

internal sealed class ProductionCertificateXmlDecryptor : IXmlDecryptor
{
    private readonly ProductionDataProtectionCertificate _certificate;

    public ProductionCertificateXmlDecryptor(IServiceProvider services)
    {
        _certificate =
            services.GetRequiredService<ProductionDataProtectionCertificate>();
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        var document = new XmlDocument();
        document.Load(new XElement("root", encryptedElement).CreateReader());

        var encryptedXml = new CertificateEncryptedXml(
            document,
            _certificate.Certificate);
        encryptedXml.DecryptDocument();

        return XElement.Load(
            document.DocumentElement!
                .FirstChild!
                .CreateNavigator()!
                .ReadSubtree());
    }

    private sealed class CertificateEncryptedXml(
        XmlDocument document,
        X509Certificate2 certificate)
        : EncryptedXml(document)
    {
        public override byte[]? DecryptEncryptedKey(EncryptedKey encryptedKey)
        {
            var keyInfo = encryptedKey.KeyInfo?.GetEnumerator();
            if (keyInfo is not null)
            {
                while (keyInfo.MoveNext())
                {
                    if (keyInfo.Current is not KeyInfoX509Data certificateInfo)
                    {
                        continue;
                    }

                    var embeddedCertificates =
                        certificateInfo.Certificates?.GetEnumerator();
                    while (embeddedCertificates?.MoveNext() == true)
                    {
                        if (embeddedCertificates.Current is X509Certificate2 embedded &&
                            string.Equals(
                                embedded.Thumbprint,
                                certificate.Thumbprint,
                                StringComparison.Ordinal))
                        {
                            using var privateKey =
                                certificate.GetRSAPrivateKey();
                            if (privateKey is null)
                            {
                                return null;
                            }

                            var useOaep =
                                encryptedKey.EncryptionMethod?.KeyAlgorithm ==
                                XmlEncRSAOAEPUrl;
                            return DecryptKey(
                                encryptedKey.CipherData.CipherValue!,
                                privateKey,
                                useOaep);
                        }
                    }
                }
            }

            return base.DecryptEncryptedKey(encryptedKey);
        }
    }
}
