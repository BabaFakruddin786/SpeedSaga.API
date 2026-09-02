using SpeedSaga.API.Infrastructure;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
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
            AppFileLogger.Exception(AppFileLogger.Category.Exception, "UNHANDLED", ex,
                $"path={context.Request.Method} {context.Request.Path}");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var message = _env.IsDevelopment()
                ? ex.Message
                : "An unexpected error occurred.";
            await context.Response.WriteAsJsonAsync(new
            {
                Success = false,
                Message = message
            });
        }
    }
}
