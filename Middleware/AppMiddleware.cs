using System.Net;
using SpeedSaga.API.Authorization;

namespace SpeedSaga.API.Middleware;

public class GeoblockMiddleware
{
    private static readonly HashSet<string> RestrictedStates =
        new(StringComparer.OrdinalIgnoreCase) { "TG", "AP", "TN", "KL", "SK", "MG", "NL" };

    private readonly RequestDelegate _next;

    public GeoblockMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/api/auth") ||
            ctx.Request.Path.StartsWithSegments("/swagger") ||
            ctx.Request.Path.StartsWithSegments("/hangfire"))
        {
            await _next(ctx);
            return;
        }

        var stateCode = ctx.User.FindFirst(AppClaimTypes.StateCode)?.Value
            ?? ctx.Request.Headers["X-State-Code"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(stateCode) && RestrictedStates.Contains(stateCode))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await ctx.Response.WriteAsJsonAsync(new
            {
                Success = false,
                Message = "Real-money gaming is not available in your state due to local regulations."
            });
            return;
        }

        await _next(ctx);
    }
}

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                Success = false,
                Message = "An unexpected error occurred."
            });
        }
    }
}
