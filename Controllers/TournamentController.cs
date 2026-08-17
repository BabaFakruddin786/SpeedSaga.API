using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Extensions;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/tournaments")]
[Authorize(Policy = Authorization.Policies.PlayerOnly)]
public class TournamentController : ControllerBase
{
    private readonly ITournamentService _tournaments;

    public TournamentController(ITournamentService tournaments) => _tournaments = tournaments;

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _tournaments.ListOpenAsync());

    [HttpPost("{tournamentId:guid}/join")]
    public async Task<IActionResult> Join(Guid tournamentId)
    {
        var result = await _tournaments.JoinAsync(User.GetPlayerId(), tournamentId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{tournamentId:guid}/play")]
    public async Task<IActionResult> Play(Guid tournamentId)
    {
        var result = await _tournaments.PlayRoundAsync(User.GetPlayerId(), tournamentId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("{tournamentId:guid}/leaderboard")]
    public async Task<IActionResult> Leaderboard(Guid tournamentId)
        => Ok(await _tournaments.GetLeaderboardAsync(tournamentId));
}
