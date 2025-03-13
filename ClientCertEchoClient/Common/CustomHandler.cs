namespace ClientCertEchoClient.Common;

public static class Keys
{
    public static readonly HttpRequestOptionsKey<UserContext> UserContext = new(nameof(UserContext));
    public static readonly HttpRequestOptionsKey<HttpContext> HttpContext = new(nameof(HttpContext));
}

public class CustomHandler : DelegatingHandler
{
    private int _counter = 0;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(Keys.UserContext, out var userContext))
        {
            request.Headers.Add("X-SourceUserContext", $"UserId={userContext.UserId};UserName={userContext.UserName};Cert={userContext.CurrentCertificate.Thumbprint}");
        }

        if (request.Options.TryGetValue(Keys.HttpContext, out var httpContext))
        {
            request.Headers.Add("X-SourceUserAgent", httpContext.Request.Headers.UserAgent.ToString());
        }

        request.Headers.Add("X-HandlerId", $"{nameof(CustomHandler)}#{GetHashCode()}");

        var count = Interlocked.Increment(ref _counter);
        request.Headers.Add("X-HandlerRequestNo", count.ToString());

        return base.SendAsync(request, cancellationToken);
    }
}
