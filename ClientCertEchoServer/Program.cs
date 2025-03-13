using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps(o =>
        {
            // Create self-signed cert for server.
            using (RSA rsa = RSA.Create())
            {
                var certReq = new CertificateRequest("CN=contoso.com", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                certReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
                X509Certificate2 cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
                if (OperatingSystem.IsWindows())
                {
#pragma warning disable SYSLIB0057 // Type or member is obsolete
                    cert = new X509Certificate2(cert.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057 // Type or member is obsolete
                }
                o.ServerCertificate = cert;
            }

            o.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            o.ClientCertificateValidation = (_, _, _) => true;
        });
    });
});

var app = builder.Build();

app.MapGet("/ClientCert", (HttpContext http) =>
{
    var cert = http.Connection.ClientCertificate;
    if (cert is null)
    {
        return Results.Json(new { error = "No client certificate provided." }, statusCode: 401);
    }

    return Results.Json(new EchoInfo( 
        ClientCertificate: new CertInfo(cert.Subject, cert.Thumbprint),
        SourceUserAgent: http.Request.Headers["X-SourceUserAgent"].ToString(),
        SourceUserContext: http.Request.Headers["X-SourceUserContext"].ToString(),
        HttpClient: http.Request.Headers["X-HttpClient"].ToString(),
        HandlerId: http.Request.Headers["X-HandlerId"].ToString(),
        HandlerRequestNo: http.Request.Headers["X-HandlerRequestNo"].SingleOrDefault() is string requestNo
                            && int.TryParse(requestNo, out var num) ? num : -1
    ));
});

app.Run();

record EchoInfo(
    CertInfo ClientCertificate,
    string SourceUserAgent,
    string SourceUserContext,
    string HttpClient,
    string HandlerId,
    int HandlerRequestNo);

record CertInfo(string Subject, string Thumbprint);