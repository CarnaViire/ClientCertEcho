using ClientCertEchoClient.Common;

var builder = WebApplication.CreateBuilder(args);

#region Common // Service registrations

builder.Services.AddSingleton<ICertificateRepository, CertificateRepository>();
builder.Services.AddScoped<UserContext>();

#endregion

#region HttpClient registrations (Keyed Singleton strategy)

// This strategy uses singleton handlers unrelated to the HttpClientFactory
builder.Services.AddKeyedSingleton(KeyedService.AnyKey, (services, key) =>
{
    var userId = (string)key!;
    var certRepo = services.GetRequiredService<ICertificateRepository>();

    // (1) Configure primary handler
    var handler = new SocketsHttpHandler()
    {
        SslOptions =
        {
            RemoteCertificateValidationCallback = delegate { return true; }, // For testing only

            // Using LocalCertificateSelectionCallback instead of the ClientCertificates collection
            // allows us to dynamically retrieve the certificate from the repository in case it has changed
            // NOTE: this executes on each connection creation
            LocalCertificateSelectionCallback = delegate { return certRepo.GetCertificate(userId); },
        },
        PooledConnectionLifetime = TimeSpan.FromMinutes(2), // From default HandlerLifetime
    };

    // (2) Configure additional handlers
    var additionalHandler = new CustomHandler() { InnerHandler = handler };

    var client = new HttpClient(additionalHandler);

    // (3) Configure HttpClient
    client.DefaultRequestHeaders.Add("X-HttpClient", $"Name={userId};Strategy=Keyed Singleton");
    client.BaseAddress = new Uri("https://localhost:5001");

    return client;
});

#endregion

var app = builder.Build();

#region Common // Endpoint setup

app.Use(UserContext.MockUserMiddleware);

app.MapGet("/ClientCert", static async (HttpContext context, UserContext userContext) =>
{
    if (!userContext.Initialized)
    {
        return Results.Unauthorized();
    }

    var client = GetHttpClient(userContext.UserId, context.RequestServices);
    var request = new HttpRequestMessage(HttpMethod.Get, "/ClientCert");

    // aux info for processing in CustomHandler
    request.Options.Set(Keys.HttpContext, context);
    request.Options.Set(Keys.UserContext, userContext);

    var response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
    var echoInfo = await response.Content.ReadFromJsonAsync<EchoInfo>();
    var actualCertInfo = echoInfo?.ClientCertificate;

    var cert = userContext.CurrentCertificate;
    var expectedCertInfo = new CertInfo(cert.Subject, cert.Thumbprint);
    if (actualCertInfo != expectedCertInfo)
    {
        Results.BadRequest($"Certificate mismatch: expected {expectedCertInfo}, got {actualCertInfo}");
    }
    
    return Results.Json(echoInfo);
});

#endregion

app.Run();

// ---------------

#region Get HttpClient instance (Keyed Singleton strategy)

static HttpClient GetHttpClient(string clientName, IServiceProvider services)
    => services.GetRequiredKeyedService<HttpClient>(clientName);

#endregion