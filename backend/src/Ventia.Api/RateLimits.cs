using System.Threading.RateLimiting;

namespace Ventia.Api;

/// <summary>limites por IP de los endpoints sin sesion (configurables para las pruebas)</summary>
public static class RateLimits
{
    public const string Auth = "auth";

    public static IServiceCollection AddVentiaRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var auth = config.GetValue("RateLimiting:AuthPerMinute", 10);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Auth, context => PerIp(context, auth));
        });
    }

    private static RateLimitPartition<string> PerIp(HttpContext context, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) });
}
