using SpeedSaga.API.Infrastructure;

namespace SpeedSaga.API.Middleware;

public class RequestLoggingMiddleware
{
    static readonly string[] SkipPrefixes = ["/hangfire", "/swagger", "/favicon.ico"];

    readonly RequestDelegate _next;

    public RequestLoggingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (ShouldSkip(path))
        {
            await _next(context);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        var method = context.Request.Method;
        var code = context.Response.StatusCode;
        var detail = $"durationMs={sw.ElapsedMilliseconds}";
        var isError = code >= 400;
        AppFileLogger.Api(method, path, code, detail, isError);
    }

    static bool ShouldSkip(string path)
    {
        foreach (var prefix in SkipPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
