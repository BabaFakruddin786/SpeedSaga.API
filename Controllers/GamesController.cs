using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Extensions;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/games")]
[Authorize(Policy = Authorization.Policies.PlayerOnly)]
public class GamesController : ControllerBase
{
    private readonly IGamePlayConfigService _playConfig;

    public GamesController(IGamePlayConfigService playConfig) => _playConfig = playConfig;

    [HttpGet]
    public IActionResult GetCatalog()
    {
        var games = new[]
        {
            new { gameId = "arrow", displayName = "Arrow Puzzle", description = "Clear arrows before time runs out.", iconKey = "arrow", sortOrder = 1, supportsFreePlay = true, supportsSinglePlayer = true, supportsTwoPlayer = true, supportsTournament = true },
            new { gameId = "car_parking", displayName = "Car Parking", description = "Slide cars and free the red vehicle.", iconKey = "car", sortOrder = 2, supportsFreePlay = true, supportsSinglePlayer = true, supportsTwoPlayer = false, supportsTournament = false },
            new { gameId = "tic_tac_toe", displayName = "Tic Tac Toe", description = "Classic 3x3 — AI or live opponent.", iconKey = "grid", sortOrder = 3, supportsFreePlay = true, supportsSinglePlayer = false, supportsTwoPlayer = true, supportsTournament = false }
        };
        return Ok(new ApiResponse<object>(true, "Games catalog", games));
    }

    [HttpGet("play-config")]
    public async Task<IActionResult> GetPlayConfig([FromQuery] string gameType = "arrow", [FromQuery] string playMode = "single")
    {
        var config = await _playConfig.GetConfigAsync(gameType, playMode);
        return Ok(new ApiResponse<GamePlayConfigDto>(true, "Play config", config));
    }
}
