using System.Collections.Concurrent;

namespace SpeedSaga.API.Infrastructure;

public record SessionMoveDto(int Index, Guid PlayerId, string Direction, int Col, int Row, float Timestamp);

/// <summary>In-memory L1 cache for real-time opponent move polling (sub-ms reads).</summary>
public class SessionMoveStore
{
    readonly ConcurrentDictionary<string, SessionMoveList> _sessions = new();

    public int AddMove(string sessionId, Guid playerId, string direction, int col, int row, float timestamp)
    {
        var list = _sessions.GetOrAdd(sessionId, _ => new SessionMoveList());
        list.Touch();
        return list.Add(playerId, direction, col, row, timestamp);
    }

    public IReadOnlyList<SessionMoveDto> GetMoves(string sessionId, Guid viewerId, int afterIndex)
    {
        if (!_sessions.TryGetValue(sessionId, out var list)) return Array.Empty<SessionMoveDto>();
        list.Touch();
        return list.GetAfter(afterIndex, viewerId);
    }

    public void Clear(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public int PurgeStale(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var removed = 0;
        foreach (var kv in _sessions)
        {
            if (kv.Value.LastTouchedUtc < cutoff && _sessions.TryRemove(kv.Key, out _))
                removed++;
        }
        return removed;
    }

    sealed class SessionMoveList
    {
        readonly object _lock = new();
        readonly List<SessionMoveDto> _moves = new();
        int _nextIndex = 1;

        public DateTime LastTouchedUtc { get; private set; } = DateTime.UtcNow;

        public void Touch() => LastTouchedUtc = DateTime.UtcNow;

        public int Add(Guid playerId, string direction, int col, int row, float timestamp)
        {
            lock (_lock)
            {
                Touch();
                var dto = new SessionMoveDto(_nextIndex++, playerId, direction, col, row, timestamp);
                _moves.Add(dto);
                return dto.Index;
            }
        }

        public IReadOnlyList<SessionMoveDto> GetAfter(int afterIndex, Guid viewerId)
        {
            lock (_lock)
            {
                Touch();
                return _moves
                    .Where(m => m.Index > afterIndex && m.PlayerId != viewerId)
                    .ToList();
            }
        }
    }
}
