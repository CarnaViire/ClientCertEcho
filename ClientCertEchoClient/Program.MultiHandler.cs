using ClientCertEchoClient.Common;
using ClientCertEchoClient.Helpers;

var builder = WebApplication.CreateBuilder(args);

#region Common // Service registrations

builder.Services.AddSingleton<ICertificateRepository, CertificateRepository>();
builder.Services.AddScoped<UserContext>();

#endregion

#region HttpClient registrations (Multi-Handler strategy)

builder.Services.AddHttpClient("Multi-Handler")
    .ConfigurePrimaryHttpMessageHandler(() => new MultiHandler(
        (userId, services) =>
        {
            // (1) Configure primary handler
            var certRepo = services.GetRequiredService<ICertificateRepository>();
            var handler = new SocketsHttpHandler()
            {
                SslOptions = { RemoteCertificateValidationCallback = delegate { return true; } }, // For testing only
                PooledConnectionLifetime = TimeSpan.FromMinutes(2), // From default HandlerLifetime
            };
            handler.SslOptions.LocalCertificateSelectionCallback =
                delegate { return certRepo.GetCertificate(userId); };
            return handler;
        },
        TimeSpan.FromMinutes(2))) // From default HandlerLifetime
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan) // Disable handler lifetime management by HttpClientFactory
    .AddHttpMessageHandler(() => new CustomHandler()) // (2) Configure additional handlers
    .ConfigureHttpClient(client => 
    {
        // (3) Configure HttpClient
        client.DefaultRequestHeaders.Add("X-HttpClient", $"Name=Multi-Handler;Strategy=Multi-Handler");
        client.BaseAddress = new Uri("https://localhost:5001");
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

#region Get HttpClient instance (Multi-Handler strategy)

static HttpClient GetHttpClient(string _, IServiceProvider services)
{
    var factory = services.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("Multi-Handler");
}

#endregion

#region MultiHandler implementation (Multi-Handler strategy)

partial class MultiHandler(
    Func<string, IServiceProvider, SocketsHttpHandler> handlerFactory,
    TimeSpan handlerLifetime)
    : HttpMessageHandler
{
    private readonly NamedCache<CachedHandler> _handlerCache = new(
        (name, sp) => new CachedHandler(handlerFactory(name, sp)),
        handlerLifetime);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CachedHandler handler = SelectHandler(request);
        return handler.SendAsyncPublic(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CachedHandler handler = SelectHandler(request);
        return handler.SendPublic(request, cancellationToken);
    }

    private CachedHandler SelectHandler(HttpRequestMessage request)
    {
        if (!request.Options.TryGetValue(Keys.HttpContext, out var httpContext))
        {
            throw new InvalidOperationException("HttpContext not found in request options.");
        }
        if (!request.Options.TryGetValue(Keys.UserContext, out var userContext))
        {
            throw new InvalidOperationException("UserContext not found in request options.");
        }

        var handler = _handlerCache.GetOrCreate(userContext.UserId, httpContext.RequestServices);
        return handler;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _handlerCache.Dispose();
        }
        base.Dispose(disposing);
    }

    // This is a HACK to allow MultiHandler to call otherwise protected Send and SendAsync methods of SocketsHttpHandler
    class CachedHandler(SocketsHttpHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        public HttpResponseMessage SendPublic(HttpRequestMessage request, CancellationToken cancellationToken)
            => base.Send(request, cancellationToken);

        public Task<HttpResponseMessage> SendAsyncPublic(HttpRequestMessage request, CancellationToken cancellationToken)
            => base.SendAsync(request, cancellationToken);
    }
}

#endregion
