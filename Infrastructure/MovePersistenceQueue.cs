using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Data.SqlClient;

namespace SpeedSaga.API.Infrastructure;

public record PendingMove(Guid SessionId, Guid PlayerId, int MoveIndex, string Direction, int Col, int Row, float Timestamp);

/// <summary>Non-blocking move persistence — gameplay never waits on DB writes.</summary>
public interface IMovePersistenceQueue
{
    void Enqueue(PendingMove move);
}

public class MovePersistenceService : BackgroundService, IMovePersistenceQueue
{
    readonly Channel<PendingMove> _channel;
    readonly ISqlConnectionFactory _db;
    readonly ILogger<MovePersistenceService> _log;

    const int BatchSize = 40;
    static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(80);

    public MovePersistenceService(ISqlConnectionFactory db, ILogger<MovePersistenceService> log)
    {
        _db = db;
        _log = log;
        _channel = Channel.CreateBounded<PendingMove>(new BoundedChannelOptions(200_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Enqueue(PendingMove move)
    {
        if (!_channel.Writer.TryWrite(move))
            _ = _channel.Writer.WriteAsync(move).AsTask();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<PendingMove>(BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                batch.Clear();
                using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timerCts.CancelAfter(FlushInterval);

                if (await _channel.Reader.WaitToReadAsync(timerCts.Token))
                {
                    while (batch.Count < BatchSize && _channel.Reader.TryRead(out var move))
                        batch.Add(move);
                }

                if (batch.Count > 0)
                    await FlushBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Move persistence batch failed; will retry on next flush");
                await Task.Delay(200, stoppingToken);
            }
        }
    }

    async Task FlushBatchAsync(List<PendingMove> batch, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(batch.Select(m => new
        {
            sessionId = m.SessionId,
            playerId = m.PlayerId,
            moveIndex = m.MoveIndex,
            direction = m.Direction,
            col = m.Col,
            row = m.Row,
            timestamp = m.Timestamp
        }));

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_RecordSessionMovesBatch", cn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@MovesJson", json);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
