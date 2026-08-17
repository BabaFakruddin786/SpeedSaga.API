using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SpeedSaga.API.Extensions;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Hubs;

[Authorize(Policy = Authorization.Policies.PlayerOnly)]
public class GameHub : Hub
{
    readonly GameConnectionTracker _tracker;
    readonly IServiceScopeFactory _scopeFactory;

    public GameHub(GameConnectionTracker tracker, IServiceScopeFactory scopeFactory)
    {
        _tracker = tracker;
        _scopeFactory = scopeFactory;
    }

    public override Task OnConnectedAsync()
    {
        var playerId = Context.User?.GetPlayerId();
        if (playerId.HasValue)
            _tracker.Register(Context.ConnectionId, playerId.Value);
        return base.OnConnectedAsync();
    }

    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        var playerId = Context.User?.GetPlayerId();
        if (playerId.HasValue)
        {
            _tracker.Register(Context.ConnectionId, playerId.Value, sessionId);
            if (Guid.TryParse(sessionId, out var sid))
                _tracker.SetSession(playerId.Value, sid);
        }
        await Clients.Group(sessionId).SendAsync("PlayerJoined", Context.ConnectionId);
    }

    public async Task SendMove(string sessionId, string direction, int col, int row, float timestamp)
    {
        await Clients.OthersInGroup(sessionId).SendAsync("OpponentMoved", direction, col, row, timestamp);
    }

    public async Task DeclareWin(string sessionId, string winnerId, int solveSecs)
    {
        await Clients.Group(sessionId).SendAsync("GameResult", new
        {
            WinnerId = winnerId,
            SolveSecs = solveSecs,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public async Task SyncTimer(string sessionId, int secondsLeft)
    {
        await Clients.Group(sessionId).SendAsync("TimerSync", secondsLeft);
    }

    public async Task JoinAsSpectator(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"spectate_{sessionId}");
    }

    public async Task BroadcastToSpectators(string sessionId, object state)
    {
        await Clients.Group($"spectate_{sessionId}").SendAsync("GameState", state);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var playerId = _tracker.GetPlayerId(Context.ConnectionId);
        var sessionId = playerId.HasValue ? _tracker.GetActiveSession(playerId.Value) : null;
        _tracker.Unregister(Context.ConnectionId);

        if (playerId.HasValue && sessionId.HasValue)
        {
            using var scope = _scopeFactory.CreateScope();
            var game = scope.ServiceProvider.GetRequiredService<IGameService>();
            await game.ForfeitPlayerAsync(sessionId.Value, playerId.Value);
            _tracker.ClearSession(playerId.Value);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
