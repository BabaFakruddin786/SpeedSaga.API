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

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int page = 1)
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        return Ok(await _players.SearchAsync(q, page));
    }

    [HttpGet("{playerId:guid}")]
    public async Task<IActionResult> Detail(Guid playerId)
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var detail = await _players.GetDetailAsync(playerId);
        return detail == null ? NotFound(new ApiResponse<object>(false, "Player not found")) : Ok(detail);
    }
}
