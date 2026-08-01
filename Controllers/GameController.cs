using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Extensions;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/game")]
[Authorize(Policy = Authorization.Policies.PlayerOnly)]
public class GameController : ControllerBase
{
    private readonly IGameService _game;
    private readonly ILevelService _level;
    private readonly IBotDetectionService _bot;

    public GameController(IGameService game, ILevelService level, IBotDetectionService bot)
    {
        _game = game;
        _level = level;
        _bot = bot;
    }

    [HttpGet("level")]
    public async Task<IActionResult> GetLevel([FromQuery] string timeMode, [FromQuery] string rewardMode)
    {
        var data = await _level.AllocateLevelAsync(User.GetPlayerId(), new AllocateLevelRequest(timeMode, rewardMode));
        return data == null
            ? NotFound(new ApiResponse<object>(false, "No level available."))
            : Ok(data);
    }

    [HttpPost("start-single")]
    public async Task<IActionResult> StartSinglePlayer([FromBody] StartSinglePlayerRequest req)
    {
        var result = await _game.StartSinglePlayerAsync(User.GetPlayerId(), req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("join-match")]
    public async Task<IActionResult> JoinMatch([FromBody] JoinMatchRequest req)
    {
        var result = await _game.JoinMatchAsync(User.GetPlayerId(), req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("result")]
    public async Task<IActionResult> SubmitResult([FromBody] SubmitResultRequest req)
    {
        var result = await _game.SubmitResultAsync(User.GetPlayerId(), req);
        if (result.Success && req.MovesJson != null)
            _ = _bot.AnalyzeSessionAsync(req.SessionId, User.GetPlayerId(), req.SolveSecs, req.TotalMoves, req.MovesJson);

        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("replay/{sessionId:guid}")]
    public async Task<IActionResult> GetReplay(Guid sessionId)
    {
        var data = await _game.GetReplayAsync(sessionId, User.GetPlayerId());
        return data == null
            ? NotFound(new ApiResponse<object>(false, "Replay not found."))
            : Ok(data);
    }
}
