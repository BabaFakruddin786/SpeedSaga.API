using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Extensions;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/player")]
[Authorize(Policy = Authorization.Policies.PlayerOnly)]
public class PlayerController : ControllerBase
{
    private readonly IPlayerService _player;

    public PlayerController(IPlayerService player) => _player = player;

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var data = await _player.GetDashboardAsync(User.GetPlayerId());
        return data == null
            ? NotFound(new ApiResponse<object>(false, "Player not found."))
            : Ok(data);
    }
}
