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
    public async Task<IActionResult> GetLevel([FromQuery] string timeMode, [FromQuery] string rewardMode, [FromQuery] long entryFeePaise = 0)
    {
        var data = await _level.AllocateLevelAsync(User.GetPlayerId(), new AllocateLevelRequest(timeMode, rewardMode, entryFeePaise));
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

    [HttpPost("start-free")]
    public async Task<IActionResult> StartFreePlay([FromBody] StartFreePlayRequest req)
    {
        var result = await _game.StartFreePlayAsync(User.GetPlayerId(), req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("join-match")]
    public async Task<IActionResult> JoinMatch([FromBody] JoinMatchRequest req)
    {
        var result = await _game.JoinMatchAsync(User.GetPlayerId(), req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("match-status")]
    public async Task<IActionResult> MatchStatus([FromQuery] string connId)
        => Ok(await _game.GetMatchStatusAsync(User.GetPlayerId(), connId));

    [HttpPost("sync-move")]
    public async Task<IActionResult> SyncMove([FromBody] SyncMoveRequest req)
    {
        await _game.SyncMoveAsync(User.GetPlayerId(), req.SessionId, req.Direction, req.Col, req.Row, req.Timestamp);
        return Ok(new ApiResponse<object>(true, "Move synced"));
    }

    [HttpGet("session-moves")]
    public async Task<IActionResult> SessionMoves([FromQuery] string sessionId, [FromQuery] int afterIndex = 0)
        => Ok(await _game.GetSessionMovesAsync(User.GetPlayerId(), sessionId, afterIndex));

    [HttpGet("session-status")]
    public async Task<IActionResult> SessionStatus([FromQuery] string sessionId)
        => Ok(await _game.GetSessionStatusAsync(User.GetPlayerId(), sessionId));

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

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _game.GetGameHistoryAsync(User.GetPlayerId(), page, pageSize));
}
