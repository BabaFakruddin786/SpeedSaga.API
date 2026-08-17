using System.Collections.Concurrent;

namespace SpeedSaga.API.Infrastructure;

/// <summary>Tracks active SignalR connections for disconnect forfeit.</summary>
public class GameConnectionTracker
{
    readonly ConcurrentDictionary<string, Guid> _connToPlayer = new();
    readonly ConcurrentDictionary<Guid, string> _playerToSession = new();

    public void Register(string connectionId, Guid playerId, string? sessionId = null)
    {
        _connToPlayer[connectionId] = playerId;
        if (!string.IsNullOrWhiteSpace(sessionId) && Guid.TryParse(sessionId, out var sid))
            _playerToSession[playerId] = sid.ToString();
    }

    public void SetSession(Guid playerId, Guid sessionId)
        => _playerToSession[playerId] = sessionId.ToString();

    public void Unregister(string connectionId)
        => _connToPlayer.TryRemove(connectionId, out _);

    public Guid? GetPlayerId(string connectionId)
        => _connToPlayer.TryGetValue(connectionId, out var id) ? id : null;

    public Guid? GetActiveSession(Guid playerId)
        => _playerToSession.TryGetValue(playerId, out var sid) && Guid.TryParse(sid, out var g) ? g : null;

    public void ClearSession(Guid playerId) => _playerToSession.TryRemove(playerId, out _);
}
