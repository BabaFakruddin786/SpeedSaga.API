using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SpeedSaga.API.Hubs;

[Authorize(Policy = Authorization.Policies.PlayerOnly)]
public class GameHub : Hub
{
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
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
        await base.OnDisconnectedAsync(exception);
    }
}
