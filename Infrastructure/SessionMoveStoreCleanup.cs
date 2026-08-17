namespace SpeedSaga.API.Infrastructure;

/// <summary>Prevents unbounded memory growth from completed game sessions.</summary>
public class SessionMoveStoreCleanup : BackgroundService
{
    readonly SessionMoveStore _store;
    readonly ILogger<SessionMoveStoreCleanup> _log;
    static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public SessionMoveStoreCleanup(SessionMoveStore store, ILogger<SessionMoveStoreCleanup> log)
    {
        _store = store;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = _store.PurgeStale(TimeSpan.FromHours(2));
                if (removed > 0)
                    _log.LogDebug("Purged {Count} stale session move caches", removed);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Session move store cleanup failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
