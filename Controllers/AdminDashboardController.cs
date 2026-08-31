using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
public class AdminDashboardController : ControllerBase
{
    readonly IAdminDashboardService _dashboard;
    readonly AdminOptions _admin;

    public AdminDashboardController(IAdminDashboardService dashboard, IOptions<AdminOptions> admin)
    {
        _dashboard = dashboard;
        _admin = admin.Value;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var stats = await _dashboard.GetStatsAsync();
        return stats == null ? NotFound() : Ok(stats);
    }

    [HttpGet("finance/daily")]
    public async Task<IActionResult> DailyFlow([FromQuery] int days = 30)
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        return Ok(await _dashboard.GetDailyFlowAsync(days));
    }

    [HttpGet("finance/by-type")]
    public async Task<IActionResult> ByType([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        return Ok(await _dashboard.GetFlowByTypeAsync(from, to));
    }
}
