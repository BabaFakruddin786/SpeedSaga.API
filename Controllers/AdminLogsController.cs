using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/admin/logs")]
public class AdminLogsController : ControllerBase
{
    readonly ILogViewerService _logs;
    readonly AdminOptions _admin;

    public AdminLogsController(ILogViewerService logs, IOptions<AdminOptions> admin)
    {
        _logs = logs;
        _admin = admin.Value;
    }

    IActionResult? RequireAdmin()
    {
        if (!AdminAuthorization.HasAdminAccess(HttpContext, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Sign in required"));
        return null;
    }

    [HttpGet("sources")]
    public IActionResult Sources()
    {
        if (RequireAdmin() is { } denied) return denied;
        return Ok(_logs.ListSources());
    }

    [HttpGet("entries")]
    public IActionResult Entries(
        [FromQuery] string source = "api",
        [FromQuery] string? date = null,
        [FromQuery] string logType = "speedsaga",
        [FromQuery] int tail = 500,
        [FromQuery] string? level = null,
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? playerId = null)
    {
        if (RequireAdmin() is { } denied) return denied;
        return Ok(_logs.ReadEntries(source, date, logType, tail, level, category, search, playerId));
    }
}
