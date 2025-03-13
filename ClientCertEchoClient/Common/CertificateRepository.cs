using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ClientCertEchoClient.Common;

public interface ICertificateRepository
{
    X509Certificate2 GetCertificate(string name);
    ValueTask<X509Certificate2> GetCertificateAsync(string name);
}

public class CertificateRepository : ICertificateRepository
{
    private readonly Dictionary<string, X509Certificate2> _certCache = [];

    public X509Certificate2 GetCertificate(string name)
    {
        if (!_certCache.TryGetValue(name, out var cert))
        {
            cert = CreateClientCertificate(name);
            _certCache[name] = cert;
        }

        return cert;
    }

    public ValueTask<X509Certificate2> GetCertificateAsync(string name)
        => ValueTask.FromResult(GetCertificate(name));

    private static X509Certificate2 CreateClientCertificate(string name)
    {
        // Create self-signed cert for client.
        using (RSA rsa = RSA.Create())
        {
            var certReq = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, false));
            certReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
            X509Certificate2 cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
            if (OperatingSystem.IsWindows())
            {
#pragma warning disable SYSLIB0057 // Type or member is obsolete
                cert = new X509Certificate2(cert.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057 // Type or member is obsolete
            }
            return cert;
        }
    }
}