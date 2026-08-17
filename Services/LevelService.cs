using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface ILevelService
{
    Task<AllocatedLevelResult?> AllocateLevelAsync(Guid playerId, AllocateLevelRequest req);
}

public class LevelService : ILevelService
{
    private readonly ISqlConnectionFactory _db;
    static readonly ConcurrentDictionary<(Guid PlayerId, string RewardMode), (string Tier, DateTime Expires)> TierCache = new();
    static readonly TimeSpan TierCacheTtl = TimeSpan.FromSeconds(45);

    public LevelService(ISqlConnectionFactory db) => _db = db;

    public async Task<AllocatedLevelResult?> AllocateLevelAsync(Guid playerId, AllocateLevelRequest req)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_AllocateLevel", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@TimeMode", req.TimeMode);
        cmd.Parameters.AddWithValue("@RewardMode", req.RewardMode);
        cmd.Parameters.AddWithValue("@EntryFeePaise", req.EntryFeePaise);

        var pId = cmd.Parameters.Add("@LevelId", SqlDbType.Int);
        pId.Direction = ParameterDirection.Output;
        var pGrid = cmd.Parameters.Add("@GridJson", SqlDbType.NVarChar, -1);
        pGrid.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        int levelId;
        if (pId.Value == DBNull.Value)
        {
            await using var fallback = new SqlCommand(@"
                SELECT TOP 1 LevelId FROM Levels WITH (NOLOCK)
                WHERE TimeMode = @TimeMode AND IsActive = 1
                ORDER BY LevelId", cn);
            fallback.Parameters.AddWithValue("@TimeMode", req.TimeMode);
            var fallbackId = await fallback.ExecuteScalarAsync();
            if (fallbackId == null || fallbackId == DBNull.Value) return null;
            levelId = (int)fallbackId;
        }
        else
        {
            levelId = (int)pId.Value;
        }

        var tier = await GetPlayerPuzzleTierAsync(cn, playerId, req.RewardMode, req.EntryFeePaise);
        return PuzzleTemplateProvider.Generate(tier, levelId, playerId);
    }

    async Task<string> GetPlayerPuzzleTierAsync(SqlConnection cn, Guid playerId, string rewardMode, long entryFeePaise = 0)
    {
        if (entryFeePaise == 0) return "Easy";

        var cacheKey = (playerId, rewardMode);
        if (TierCache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTime.UtcNow)
            return cached.Tier;

        await using var cmd = new SqlCommand(@"
            SELECT COUNT(*) FROM GameSessions WITH (NOLOCK)
            WHERE Player1Id = @PlayerId AND GameMode LIKE 'SinglePlayer%'
              AND RewardMode = @RewardMode AND Status IN ('Active','Complete')", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@RewardMode", rewardMode);
        var games = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        string tier;
        if (rewardMode == "5x")
        {
            if (games < 2) tier = "Medium";
            else if (games < 5) tier = "Hard";
            else tier = "SuperHard";
        }
        else if (games < 3) tier = "Easy";
        else if (games < 8) tier = "Medium";
        else if (games < 15) tier = "Hard";
        else tier = "SuperHard";

        TierCache[cacheKey] = (tier, DateTime.UtcNow.Add(TierCacheTtl));
        return tier;
    }
}
