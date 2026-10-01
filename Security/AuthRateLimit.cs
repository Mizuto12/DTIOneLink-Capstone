using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DTIOneLink.Security
{
    // A per-network limit on sign-in, code and password form posts, on top
    // of the per-account limits (lockout, code tries, code requests). Staff in
    // the same office share one public IP, so the limit is generous: it stops
    // automated guessing, not people.
    public static class AuthRateLimit
    {
        public const string Policy = "auth";
        public const int PermitsPerMinute = 30;

        public static IServiceCollection AddAuthRateLimit(this IServiceCollection services)
        {
            return services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                    await context.HttpContext.Response.WriteAsync(
                        "Too many attempts from this network. Please wait one minute, then go back and try again.",
                        cancellationToken);
                };

                options.AddPolicy(Policy, httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PermitsPerMinute,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
            });
        }
    }
}
