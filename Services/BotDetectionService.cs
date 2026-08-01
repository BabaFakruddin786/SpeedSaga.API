using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;

namespace SpeedSaga.API.Services;

public interface IBotDetectionService
{
    Task AnalyzeSessionAsync(Guid sessionId, Guid playerId, int solveSecs, int totalMoves, string movesJson);
    Task RunBotScan();
}

public class BotDetectionService : IBotDetectionService
{
    private readonly ISqlConnectionFactory _db;

    public BotDetectionService(ISqlConnectionFactory db) => _db = db;

    public async Task AnalyzeSessionAsync(Guid sessionId, Guid playerId, int solveSecs, int totalMoves, string movesJson)
    {
        var flagScore = 0;
        string? reason = null;

        if (solveSecs > 0 && solveSecs < 3)
        {
            flagScore += 80;
            reason = $"Solved in {solveSecs}s — impossibly fast";
        }

        if (totalMoves > 0 && solveSecs > 0 && totalMoves / (double)solveSecs > 5)
        {
            flagScore += 40;
            reason ??= "Unusually high move rate detected";
        }

        if (flagScore > 0 && reason != null)
            await FlagPlayerAsync(playerId, sessionId, reason, flagScore);
    }

    public async Task RunBotScan()
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();

        const string sql = @"
            SELECT PlayerId FROM PlayerStats
            WHERE TotalGames >= 50 AND WinRatePct > 90
              AND PlayerId NOT IN (
                  SELECT PlayerId FROM BotFlagLog
                  WHERE FlagReason LIKE '%high win rate%' AND CreatedAt > DATEADD(DAY, -1, GETDATE())
              )";

        await using var cmd = new SqlCommand(sql, cn);
        await using var rdr = await cmd.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await rdr.ReadAsync())
            ids.Add((Guid)rdr[0]);
        await rdr.CloseAsync();

        foreach (var id in ids)
            await FlagPlayerAsync(id, null, "Win rate >90% after 50+ games — possible bot", 60);
    }

    private async Task FlagPlayerAsync(Guid playerId, Guid? sessionId, string reason, int score)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_FlagSuspiciousPlayer", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@SessionId", (object?)sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FlagReason", reason);
        cmd.Parameters.AddWithValue("@FlagScore", score);
        await cmd.ExecuteNonQueryAsync();
    }
}
