using System.Diagnostics;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using ClientCertEchoClient.Common;

var builder = WebApplication.CreateBuilder(args);

#region Common // Service registrations

builder.Services.AddSingleton<ICertificateRepository, CertificateRepository>();
builder.Services.AddScoped<UserContext>();

#endregion

#region HttpClient registrations (HttpClientDefaults strategy)

builder.Services.ConfigureHttpClientDefaults(b => b
    .UseSocketsHttpHandler((h, services) =>
    {
        // (1.1) Configure primary handler (for all names)
        h.SslOptions.RemoteCertificateValidationCallback = delegate { return true; }; // For testing only
        h.PooledConnectionLifetime = TimeSpan.FromMinutes(2); // From default HandlerLifetime
    })
    .AddHttpMessageHandler(() => new CustomHandler()) // (2) Configure additional handlers
    .ConfigureHttpClient(client => 
    {
        // (3.1) Configure HttpClient (for all names)
        client.BaseAddress = new Uri("https://localhost:5001");
    })
);

// This is a HACK to get access to the name of the client from the callback
// See https://github.com/dotnet/runtime/issues/110167
builder.Services.ConfigureOptions<ClientCertificateConfigurator>();

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

#region Get HttpClient instance (HttpClientDefaults strategy)

static HttpClient GetHttpClient(string clientName, IServiceProvider services)
{
    var factory = services.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient(clientName);
}

#endregion

#region HACK: ClientCertificateConfigurator (HttpClientDefaults strategy)

// This is a HACK to get access to the name of the client from the callback
// See https://github.com/dotnet/runtime/issues/110167
// NOTE: beyond this specific use case, directly using HttpMessageHandlerBuilder
// in callbacks is NOT recommended

class ClientCertificateConfigurator : IConfigureNamedOptions<HttpClientFactoryOptions>
{
    // This is called for each named client on handler chain creation (= at max once per HandlerLifetime)
    public void Configure(string? name, HttpClientFactoryOptions options)
    {
        if (name is null)
        {
            return;
        }

        string userId = name;

        // (1.2) Configure primary handler (client name access)
        options.HttpMessageHandlerBuilderActions.Add(builder =>
        {
            var certRepo = builder.Services.GetRequiredService<ICertificateRepository>();

            // This should not happen because of the UseSocketsHttpHandler call in ConfigureHttpClientDefaults
            Debug.Assert(builder.PrimaryHandler is SocketsHttpHandler, "Primary handler is not SocketsHttpHandler");

            SocketsHttpHandler primaryHandler = (SocketsHttpHandler)builder.PrimaryHandler;

            // Using LocalCertificateSelectionCallback instead of the ClientCertificates collection
            // allows us to dynamically retrieve the certificate from the repository in case it has changed
            // NOTE: this executes on each connection creation

            primaryHandler.SslOptions.LocalCertificateSelectionCallback =
                delegate { return certRepo.GetCertificate(userId); };
        });

        options.HttpClientActions.Add(client =>
        {
            // (3.2) Configure client (client name access)
            client.DefaultRequestHeaders.Add("X-HttpClient", $"Name={userId};Strategy=HttpClientDefaults");
        });
    }

    public void Configure(HttpClientFactoryOptions options) { }
}

#endregion