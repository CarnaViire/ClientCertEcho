using System.Diagnostics;
using ClientCertEchoClient.Common;

var builder = WebApplication.CreateBuilder(args);

#region Common // Service registrations

builder.Services.AddSingleton<ICertificateRepository, CertificateRepository>();
builder.Services.AddScoped<UserContext>();

#endregion

#region HttpClient registrations (Lazy Init strategy)

builder.Services.ConfigureHttpClientDefaults(b => b
    .UseSocketsHttpHandler((h, services) =>
    {
        // (1.1) Configure primary handler (for all names)
        h.SslOptions.RemoteCertificateValidationCallback = delegate { return true; }; // For testing only
        h.PooledConnectionLifetime = TimeSpan.FromMinutes(2); // From default HandlerLifetime
    })
    .AddHttpMessageHandler(() => new LazyInitHandler()) // (1.2) Set up lazy client cert init
    .AddHttpMessageHandler(() => new CustomHandler()) // (2) Configure additional handlers
    .ConfigureHttpClient(client => 
    {
        // (3.1) Configure HttpClient (for all names)
        client.BaseAddress = new Uri("https://localhost:5001");
    })
);

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

#region Get HttpClient instance (Lazy Init strategy)

static HttpClient GetHttpClient(string clientName, IServiceProvider services)
{
    var factory = services.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient(clientName);
}

#endregion

#region HACK: Client certificate lazy init (Lazy Init strategy)

// This handler is used to lazily initialize the client certificate on the first request
class LazyInitHandler : DelegatingHandler
{
    private string? _clientName;
    private readonly object _lock = new();

    private bool Initialized => _clientName is not null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        InitializeClientCertificate(request);

        // (3.2)-ish: We can't access the client name from the HttpClient level, so
        // we have to add it to the request headers here
        request.Headers.Add("X-HttpClient", $"Name={_clientName};Strategy=Lazy Init");

        return await base.SendAsync(request, cancellationToken);
    }

    private void InitializeClientCertificate(HttpRequestMessage request)
    {
        // This is a HACK to get access to the name of the client
        // NOTE: the name of the client must be equal to the userId

        if (Initialized)
        {
            return;
        }

        lock (_lock)
        {
            if (Initialized)
            {
                return;
            }

            if (!request.Options.TryGetValue(Keys.HttpContext, out var httpContext))
            {
                throw new InvalidOperationException("HttpContext not found in request options.");
            }
            if (!request.Options.TryGetValue(Keys.UserContext, out var userContext))
            {
                throw new InvalidOperationException("UserContext not found in request options.");
            }

            // (1.3) Execute lazy client cert init
            InitializeClientCertificate(userContext.UserId, httpContext.RequestServices);
        }
    }

    private void InitializeClientCertificate(string userId, IServiceProvider services)
    {
        Debug.Assert(Monitor.IsEntered(_lock), "Lock must be held before calling this method");
        Debug.Assert(!Initialized, "Must not be initialized before calling this method");

        var certRepo = services.GetRequiredService<ICertificateRepository>();
        var primaryHandler = GetPrimaryHandler();

        // Using LocalCertificateSelectionCallback instead of the ClientCertificates collection
        // allows us to dynamically retrieve the certificate from the repository in case it has changed
        // NOTE: this executes on each connection creation
        primaryHandler.SslOptions.LocalCertificateSelectionCallback =
            delegate { return certRepo.GetCertificate(userId); };

        _clientName = userId;
    }

    private SocketsHttpHandler GetPrimaryHandler()
    {
        SocketsHttpHandler? primaryHandler = null;

        // Traverse the handler chain to find the primary SocketsHttpHandler
        var innerHandler = InnerHandler;
        while (innerHandler is not null)
        {
            if (innerHandler is SocketsHttpHandler shh)
            {
                primaryHandler = shh;
                break;
            }
            if (innerHandler is not DelegatingHandler delegatingHandler)
            {
                // This should not happen because of the UseSocketsHttpHandler call in ConfigureHttpClientDefaults
                throw new InvalidOperationException("Inner handler is not SocketsHttpHandler");
            }

            innerHandler = delegatingHandler.InnerHandler;
        }

        if (primaryHandler is null)
        {
            // This should not happen because of the UseSocketsHttpHandler call in ConfigureHttpClientDefaults
            throw new InvalidOperationException("Primary handler is not set");
        }

        return primaryHandler;
    }
}

#endregion