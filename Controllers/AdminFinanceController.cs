using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/admin/finance")]
public class AdminFinanceController : ControllerBase
{
    readonly IAdminFinanceService _finance;
    readonly AdminOptions _admin;

    public AdminFinanceController(IAdminFinanceService finance, IOptions<AdminOptions> admin)
    {
        _finance = finance;
        _admin = admin.Value;
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions(
        [FromQuery] string? type,
        [FromQuery] Guid? playerId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1)
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        return Ok(await _finance.ListTransactionsAsync(type, playerId, from, to, page));
    }

    [HttpGet("top-depositors")]
    public async Task<IActionResult> TopDepositors([FromQuery] int days = 30, [FromQuery] int limit = 10)
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        return Ok(await _finance.TopDepositorsAsync(days, limit));
    }

    [HttpGet("players/{playerId:guid}/daily")]
    public async Task<IActionResult> PlayerDaily(Guid playerId, [FromQuery] int days = 30)
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        return Ok(await _finance.PlayerFinanceDailyAsync(playerId, days));
    }
}
