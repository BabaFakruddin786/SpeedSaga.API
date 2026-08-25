using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IGamePlayConfigService
{
    Task<GamePlayConfigDto> GetConfigAsync(string gameType, string playMode);
    Task<(bool Ok, string Error)> ValidateSinglePlayerAsync(string gameType, string timeMode, string rewardMode, long entryFeePaise);
    Task<(bool Ok, string Error)> ValidateTwoPlayerAsync(string gameType, int timeSecs, long entryFeePaise);
    Task<(bool Ok, string Error)> ValidateFreePlayAsync(string gameType, string timeMode);
    long ComputeRewardPaise(long entryFeePaise, string rewardMode, GamePlayConfigDto config);
    int ComputeTimeLimitSecs(string timeMode, string rewardMode, GamePlayConfigDto config);
    int ComputeTwoPlayerRewardPaise(long entryFeePaise, GamePlayConfigDto config);
}

public class GamePlayConfigService : IGamePlayConfigService
{
    readonly ISqlConnectionFactory _db;
    readonly IMemoryCache _cache;
    static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    public GamePlayConfigService(ISqlConnectionFactory db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<GamePlayConfigDto> GetConfigAsync(string gameType, string playMode)
    {
        gameType = GameTypes.Normalize(gameType);
        playMode = NormalizePlayMode(playMode);
        var key = $"playcfg:{gameType}:{playMode}";
        if (_cache.TryGetValue(key, out GamePlayConfigDto? cached) && cached != null)
            return cached;

        var config = await LoadConfigAsync(gameType, playMode);
        _cache.Set(key, config, CacheTtl);
        return config;
    }

    public async Task<(bool Ok, string Error)> ValidateSinglePlayerAsync(string gameType, string timeMode, string rewardMode, long entryFeePaise)
    {
        var cfg = await GetConfigAsync(gameType, "single");
        if (!cfg.RewardModes.Any(r => string.Equals(r.Code, rewardMode, StringComparison.OrdinalIgnoreCase)))
            return (false, "Reward mode is not available.");
        if (!cfg.TimeModes.Any(t => string.Equals(t.Code, timeMode, StringComparison.OrdinalIgnoreCase)))
            return (false, "Time mode is not available.");
        if (!cfg.EntryFees.Any(f => f.EntryFeePaise == entryFeePaise))
            return (false, "Entry fee is not available.");
        return (true, "");
    }

    public async Task<(bool Ok, string Error)> ValidateTwoPlayerAsync(string gameType, int timeSecs, long entryFeePaise)
    {
        var cfg = await GetConfigAsync(gameType, "two_player");
        if (!cfg.EntryFees.Any(f => f.EntryFeePaise == entryFeePaise))
            return (false, "Entry fee is not available.");
        if (!cfg.TimeModes.Any(t => t.BaseSeconds == timeSecs))
            return (false, "Time limit is not available.");
        return (true, "");
    }

    public async Task<(bool Ok, string Error)> ValidateFreePlayAsync(string gameType, string timeMode)
    {
        var cfg = await GetConfigAsync(gameType, "free");
        if (cfg.TimeModes.Any(t => string.Equals(t.Code, timeMode, StringComparison.OrdinalIgnoreCase)))
            return (true, "");
        if (DefaultTimeModes("free").Any(t => string.Equals(t.Code, timeMode, StringComparison.OrdinalIgnoreCase)))
            return (true, "");
        return (false, "Time mode is not available.");
    }

    public long ComputeRewardPaise(long entryFeePaise, string rewardMode, GamePlayConfigDto config)
    {
        var mode = config.RewardModes.FirstOrDefault(r =>
            string.Equals(r.Code, rewardMode, StringComparison.OrdinalIgnoreCase));
        var mult = mode?.RewardMultiplier ?? 3m;
        return (long)Math.Round(entryFeePaise * mult, MidpointRounding.AwayFromZero);
    }

    public int ComputeTimeLimitSecs(string timeMode, string rewardMode, GamePlayConfigDto config)
    {
        var tm = config.TimeModes.FirstOrDefault(t =>
            string.Equals(t.Code, timeMode, StringComparison.OrdinalIgnoreCase));
        var rm = config.RewardModes.FirstOrDefault(r =>
            string.Equals(r.Code, rewardMode, StringComparison.OrdinalIgnoreCase));
        var baseSecs = tm?.BaseSeconds ?? 60;
        var factor = rm?.TimeLimitFactor ?? 0.75m;
        return Math.Max(1, (int)Math.Floor(baseSecs * factor));
    }

    public int ComputeTwoPlayerRewardPaise(long entryFeePaise, GamePlayConfigDto config)
        => (int)Math.Floor(entryFeePaise * 2m * config.TwoPlayerPoolPercent / 100m);

    async Task<GamePlayConfigDto> LoadConfigAsync(string gameType, string playMode)
    {
        try
        {
            var rewards = new List<GamePlayRewardModeDto>();
            var times = new List<GamePlayTimeModeDto>();
            var fees = new List<GamePlayEntryFeeDto>();
            var poolPercent = 85;

            await using var cn = _db.CreateConnection();
            await cn.OpenAsync();

            await using (var cmd = new SqlCommand(@"
                SELECT RewardModeCode, DisplayName, HintText, RewardMultiplier, TimeLimitFactor, SortOrder
                FROM GamePlayRewardModes
                WHERE GameType = @GameType AND PlayMode = @PlayMode AND IsActive = 1
                ORDER BY SortOrder, RewardModeId", cn))
            {
                cmd.Parameters.AddWithValue("@GameType", gameType);
                cmd.Parameters.AddWithValue("@PlayMode", playMode);
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    rewards.Add(new GamePlayRewardModeDto(
                        rdr.GetString(0),
                        rdr.GetString(1),
                        rdr.IsDBNull(2) ? null : rdr.GetString(2),
                        rdr.GetDecimal(3),
                        rdr.GetDecimal(4),
                        rdr.GetInt32(5)));
                }
            }

            await using (var cmd = new SqlCommand(@"
                SELECT TimeModeCode, DisplayLabel, BaseSeconds, SortOrder
                FROM GamePlayTimeModes
                WHERE GameType = @GameType AND PlayMode = @PlayMode AND IsActive = 1
                ORDER BY SortOrder, TimeModeId", cn))
            {
                cmd.Parameters.AddWithValue("@GameType", gameType);
                cmd.Parameters.AddWithValue("@PlayMode", playMode);
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    times.Add(new GamePlayTimeModeDto(
                        rdr.GetString(0),
                        rdr.GetString(1),
                        rdr.GetInt32(2),
                        rdr.GetInt32(3)));
                }
            }

            await using (var cmd = new SqlCommand(@"
                SELECT EntryFeePaise, SortOrder
                FROM GamePlayEntryFees
                WHERE GameType = @GameType AND PlayMode = @PlayMode AND IsActive = 1
                ORDER BY SortOrder, EntryFeeId", cn))
            {
                cmd.Parameters.AddWithValue("@GameType", gameType);
                cmd.Parameters.AddWithValue("@PlayMode", playMode);
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    fees.Add(new GamePlayEntryFeeDto(rdr.GetInt64(0), rdr.GetInt32(1)));
                }
            }

            try
            {
                await using var cmd = new SqlCommand(
                    "SELECT SettingValue FROM GamePlaySettings WHERE SettingKey = 'two_player_pool_percent'", cn);
                var val = await cmd.ExecuteScalarAsync();
                if (val != null && int.TryParse(val.ToString(), out var pct))
                    poolPercent = pct;
            }
            catch (SqlException)
            {
                // GamePlaySettings optional until migration 024 is applied.
            }

            if (rewards.Count == 0)
                rewards.AddRange(DefaultRewardModes(gameType, playMode));
            if (times.Count == 0)
                times.AddRange(DefaultTimeModes(playMode));
            else if (playMode == "free")
            {
                foreach (var d in DefaultTimeModes("free"))
                {
                    if (!times.Any(t => string.Equals(t.Code, d.Code, StringComparison.OrdinalIgnoreCase)))
                        times.Add(d);
                }
            }
            if (fees.Count == 0 && playMode != "free")
                fees.AddRange(DefaultEntryFees());

            return new GamePlayConfigDto(gameType, playMode, rewards, times, fees, poolPercent);
        }
        catch (SqlException)
        {
            return BuildDefaultConfig(gameType, playMode);
        }
    }

    static GamePlayConfigDto BuildDefaultConfig(string gameType, string playMode) =>
        new(
            gameType,
            playMode,
            DefaultRewardModes(gameType, playMode).ToList(),
            DefaultTimeModes(playMode).ToList(),
            playMode == "free" ? new List<GamePlayEntryFeeDto>() : DefaultEntryFees().ToList(),
            85);

    static string NormalizePlayMode(string playMode) => playMode?.Trim().ToLowerInvariant() switch
    {
        "single" or "single_player" or "single-player" => "single",
        "two_player" or "two-player" or "twoplayer" => "two_player",
        "free" or "free_play" or "free-play" => "free",
        _ => "single"
    };

    static IEnumerable<GamePlayRewardModeDto> DefaultRewardModes(string gameType, string playMode)
    {
        if (playMode != "single") yield break;
        if (gameType == GameTypes.CarParking)
        {
            yield return new("3x", "3x Rewards", "More time to solve the lot", 3m, 0.75m, 1);
            yield return new("5x", "5x Rewards", "Higher reward · shorter time", 5m, 0.5m, 2);
            yield break;
        }
        if (gameType == GameTypes.Arrow)
        {
            yield return new("1x", "1x Rewards", "Standard time · 1× entry reward", 1m, 1m, 1);
            yield return new("3x", "3x Rewards", "3× = more time to solve · lower reward multiplier", 3m, 0.75m, 2);
            yield return new("5x", "5x Rewards", "5× = higher reward · shorter time limit", 5m, 0.5m, 3);
        }
    }

    static IEnumerable<GamePlayTimeModeDto> DefaultTimeModes(string playMode)
    {
        yield return new("1min", "1 Min", 60, 1);
        yield return new("2min", "2 Min", 120, 2);
        yield return new("3min", "3 Min", 180, 3);
        yield return new("4min", "4 Min", 240, 4);
        yield return new("5min", "5 Min", 300, 5);
    }

    static IEnumerable<GamePlayEntryFeeDto> DefaultEntryFees()
    {
        var amounts = new long[] { 5000, 10000, 20000, 30000, 40000, 50000, 75000, 100000,
            150000, 200000, 250000, 300000, 400000, 500000, 750000, 1000000 };
        for (int i = 0; i < amounts.Length; i++)
            yield return new(amounts[i], i + 1);
    }
}
