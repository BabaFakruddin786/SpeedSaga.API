using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/admin/players")]
public class AdminPlayersController : ControllerBase
{
    readonly IAdminPlayerService _players;
    readonly AdminOptions _admin;

    public AdminPlayersController(IAdminPlayerService players, IOptions<AdminOptions> admin)
    {
        _players = players;
        _admin = admin.Value;
    }

    IActionResult? RequireAdmin()
    {
        if (!AdminAuthorization.HasAdminAccess(HttpContext, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Sign in required"));
        return null;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int page = 1)
    {
        if (RequireAdmin() is { } denied) return denied;
        return Ok(await _players.SearchAsync(q, page));
    }

    [HttpGet("{playerId:guid}")]
    public async Task<IActionResult> Detail(Guid playerId)
    {
        if (RequireAdmin() is { } denied) return denied;
        var detail = await _players.GetDetailAsync(playerId);
        return detail == null ? NotFound(new ApiResponse<object>(false, "Player not found")) : Ok(detail);
    }

    [HttpPost("{playerId:guid}/ban")]
    public async Task<IActionResult> SetBan(Guid playerId, [FromBody] SetPlayerBanRequest req, CancellationToken ct = default)
    {
        if (RequireAdmin() is { } denied) return denied;
        var result = await _players.SetBanAsync(playerId, req.IsBanned, req.Reason, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{playerId:guid}/purge-data")]
    public async Task<IActionResult> PurgePlayerData(Guid playerId, [FromServices] IAdminTestDataService testData, CancellationToken ct = default)
    {
        if (!AdminAuthorization.HasSuperAdminAccess(HttpContext, _admin))
            return AdminAuthorization.HasAdminAccess(HttpContext, _admin)
                ? StatusCode(403, new ApiResponse<object>(false, "Super Admin access required."))
                : Unauthorized(new ApiResponse<object>(false, "Sign in required"));

        var result = await testData.DeletePlayerAsync(playerId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

public record SetPlayerBanRequest(bool IsBanned, string? Reason);
