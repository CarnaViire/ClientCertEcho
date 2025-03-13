using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;

namespace ClientCertEchoClient.Common;

public class UserContext(ICertificateRepository certRepo)
{
    public ClaimsPrincipal User { get; set; } = null!;
    public string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public string UserName => User.Identity!.Name!;
    public X509Certificate2 CurrentCertificate => certRepo.GetCertificate(UserId);

    public bool Initialized => User is not null;

    public static Task MockUserMiddleware(HttpContext context, RequestDelegate next)
    {
        var userContext = context.RequestServices.GetRequiredService<UserContext>();
        if (!userContext.Initialized)
        {
            // pretend the incoming request has a user
            var userId = GetMockUserIdOrDefault(context); 
            userContext.InitPrivate(userId);
        }
        context.User = userContext.User;

        return next(context);
    }

    public static UserContext GetCurrent(IServiceProvider services)
    {
        UserContext userContext = services.GetRequiredService<UserContext>();
        if (!userContext.Initialized)
        {
            userContext.InitPrivate(userId: null); // anonymous
        }
        return userContext;
    }

    private void InitPrivate(string? userId)
    {
        if (Initialized)
        {
            throw new InvalidOperationException("UserContext already initialized");
        }

        User = CreateMockUser(userId);
    }

    public static string? GetMockUserIdOrDefault(HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }

        var userId = context.Request.Query["userId"].FirstOrDefault();
        userId ??= context.GetRouteValue("userId") as string;
        userId ??= context.Request.Headers["X-UserId"].FirstOrDefault();
        userId ??= context.Connection.ClientCertificate?.Subject;

        return userId;
    }

    private static ClaimsPrincipal CreateMockUser(string? userId)
    {
        return new ClaimsPrincipal(
            new ClaimsIdentity([
                new Claim(ClaimTypes.Name, userId?.ToUpperInvariant() ?? "(anonymous)"),
                new Claim(ClaimTypes.NameIdentifier, userId ?? Guid.NewGuid().ToString())
            ],
            "CustomAuth"));
    }
}