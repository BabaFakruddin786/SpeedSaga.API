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

    IActionResult? RequireSuperAdmin()
    {
        if (!AdminAuthorization.HasSuperAdminAccess(HttpContext, _admin))
            return AdminAuthorization.HasAdminAccess(HttpContext, _admin)
                ? Forbid()
                : Unauthorized(new ApiResponse<object>(false, "Sign in required"));
        return null;
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions(
        [FromQuery] string? type,
        [FromQuery] Guid? playerId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        return Ok(await _finance.ListTransactionsAsync(type, playerId, from, to, page));
    }

    [HttpGet("top-depositors")]
    public async Task<IActionResult> TopDepositors([FromQuery] int days = 30, [FromQuery] int limit = 10)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        return Ok(await _finance.TopDepositorsAsync(days, limit));
    }

    [HttpGet("players/{playerId:guid}/daily")]
    public async Task<IActionResult> PlayerDaily(Guid playerId, [FromQuery] int days = 30)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        return Ok(await _finance.PlayerFinanceDailyAsync(playerId, days));
    }

    [HttpGet("withdrawals/pending")]
    public async Task<IActionResult> PendingWithdrawals([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        return Ok(await _finance.ListPendingWithdrawalsAsync(page, ct));
    }

    [HttpPost("withdrawals/{txnId:guid}/process")]
    public async Task<IActionResult> ProcessWithdrawal(Guid txnId, [FromBody] ProcessWithdrawalRequest req, CancellationToken ct = default)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        var result = await _finance.ProcessWithdrawalAsync(txnId, req.Action, req.GatewayRef, req.Remarks, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

public record ProcessWithdrawalRequest(string Action, string? GatewayRef, string? Remarks);
