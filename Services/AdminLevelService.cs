using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public record LevelSummaryDto(
    int TotalLevels,
    int ActiveLevels,
    int InactiveLevels,
    decimal ActivePercent,
    int MinLevelId,
    int MaxLevelId,
    int LegacyInactiveCount,
    int ModernActiveCount,
    int OtherInactiveCount,
    int LegacyActiveCount,
    int PurgeableLegacyCount,
    int PurgeBlockedLegacyCount,
    string PoolHealth,
    string Recommendation,
    IReadOnlyList<LevelBucketDto> ByTimeMode,
    IReadOnlyList<LevelBucketDto> ByTier,
    IReadOnlyList<ArrowTierInfoDto> ArrowTiers,
    IReadOnlyList<TierProgressionDto> TierProgression);

public record TierProgressionDto(string RewardMode, string Tier, int GamesFrom, int? GamesTo, int ArrowCount);

public record LevelBucketDto(string Key, int LevelCount, int ActiveCount);

public record ArrowTierInfoDto(string Tier, int ArrowCount, string Description);

public record LevelRowDto(
    int LevelId,
    string TimeMode,
    string PuzzleTier,
    int DifficultyScore,
    int ArrowCount,
    int GridCols,
    int GridRows,
    int Seed,
    bool IsActive,
    DateTime CreatedAt,
    string SlotType,
    string StatusNote);

public record LevelListResult(int TotalCount, IReadOnlyList<LevelRowDto> Items);

public interface IAdminLevelService
{
    Task<LevelSummaryDto> GetSummaryAsync(CancellationToken ct = default);
    Task<LevelListResult> ListAsync(string? timeMode, string? puzzleTier, bool? isActive, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<object>> ExpandPoolAsync(int targetTotal, CancellationToken ct = default);
    Task<ApiResponse<object>> PurgeInactiveAsync(bool legacyOnly = true, CancellationToken ct = default);
    Task<ApiResponse<object>> SetActiveAsync(int levelId, bool isActive, CancellationToken ct = default);
}

public class AdminLevelService : IAdminLevelService
{
    readonly ISqlConnectionFactory _db;

    static readonly ArrowTierInfoDto[] ArrowTiers =
    [
        new("Easy", 30, "Entry difficulty — 30 arrows on 32×32 board"),
        new("Medium", 50, "Mid difficulty — 50 arrows"),
        new("Hard", 80, "Advanced — 80 arrows"),
        new("SuperHard", 120, "Expert — 120 arrows"),
    ];

    static readonly TierProgressionDto[] TierProgression =
    [
        new("3x", "Easy", 0, 2, 30),
        new("3x", "Medium", 3, 7, 50),
        new("3x", "Hard", 8, 14, 80),
        new("3x", "SuperHard", 15, null, 120),
        new("5x", "Medium", 0, 1, 50),
        new("5x", "Hard", 2, null, 80),
        new("Free play", "Easy", 0, null, 30),
    ];

    public AdminLevelService(ISqlConnectionFactory db) => _db = db;

    public async Task<LevelSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminGetLevelSummary", cn) { CommandType = CommandType.StoredProcedure };

        int total = 0, active = 0, inactive = 0;
        int minId = 0, maxId = 0, legacyInactive = 0, modernActive = 0, otherInactive = 0, legacyActive = 0;
        int purgeableLegacy = 0, purgeBlockedLegacy = 0;
        var byTime = new List<LevelBucketDto>();
        var byTier = new List<LevelBucketDto>();

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (await rdr.ReadAsync(ct))
        {
            total = (int)rdr["TotalLevels"];
            active = (int)rdr["ActiveLevels"];
            inactive = (int)rdr["InactiveLevels"];
        }

        if (await rdr.NextResultAsync(ct))
        {
            while (await rdr.ReadAsync(ct))
                byTime.Add(new LevelBucketDto(rdr["TimeMode"]?.ToString() ?? "", (int)rdr["LevelCount"], (int)rdr["ActiveCount"]));
        }

        if (await rdr.NextResultAsync(ct))
        {
            while (await rdr.ReadAsync(ct))
                byTier.Add(new LevelBucketDto(rdr["PuzzleTier"]?.ToString() ?? "", (int)rdr["LevelCount"], (int)rdr["ActiveCount"]));
        }

        if (await rdr.NextResultAsync(ct) && await rdr.ReadAsync(ct))
        {
            minId = rdr["MinLevelId"] == DBNull.Value ? 0 : (int)rdr["MinLevelId"];
            maxId = rdr["MaxLevelId"] == DBNull.Value ? 0 : (int)rdr["MaxLevelId"];
            legacyInactive = (int)rdr["LegacyInactiveCount"];
            modernActive = (int)rdr["ModernActiveCount"];
            otherInactive = (int)rdr["OtherInactiveCount"];
            legacyActive = (int)rdr["LegacyActiveCount"];
            purgeableLegacy = (int)rdr["PurgeableLegacyCount"];
            purgeBlockedLegacy = (int)rdr["PurgeBlockedLegacyCount"];
        }

        var activePct = total > 0 ? Math.Round(active * 100m / total, 1) : 0m;
        var (health, recommendation) = BuildHealthAdvice(total, active, modernActive, legacyInactive, purgeableLegacy, byTime);

        return new LevelSummaryDto(
            total, active, inactive, activePct, minId, maxId,
            legacyInactive, modernActive, otherInactive, legacyActive,
            purgeableLegacy, purgeBlockedLegacy,
            health, recommendation, byTime, byTier, ArrowTiers, TierProgression);
    }

    static (string Health, string Recommendation) BuildHealthAdvice(
        int total, int active, int modernActive, int legacyInactive, int purgeableLegacy, List<LevelBucketDto> byTime)
    {
        if (total == 0)
            return ("Empty", "No level slots exist. Expand the pool to at least 500 slots.");

        var minActivePerMode = byTime.Count > 0 ? byTime.Min(t => t.ActiveCount) : 0;
        if (modernActive < 100)
            return ("Low", $"Only {modernActive} modern runtime slots are active. Expand pool to 1000+ for better player variety.");

        if (minActivePerMode < 20)
            return ("Fair", $"Some time modes have only {minActivePerMode} active slots. Consider expanding to 1500+.");

        if (legacyInactive > total * 0.5m && purgeableLegacy > 0)
            return ("Healthy", $"{purgeableLegacy} unused legacy slots can be purged to clean the pool. Active runtime pool is healthy at {active} slots.");

        if (legacyInactive > total * 0.5m)
            return ("Healthy", $"{legacyInactive} legacy placeholders are inactive (expected). Active runtime pool is healthy at {active} slots.");

        return ("Healthy", $"Pool has {active} active rotation slots across {byTime.Count} time modes. Expand when players report repetition.");
    }

    static (string SlotType, string StatusNote) DescribeSlot(bool isActive, int arrowCount, int gridCols)
    {
        var isLegacy = arrowCount < 12 || gridCols < 32;
        if (!isActive && isLegacy)
            return ("Legacy placeholder", "Retired auto-generated row from early migrations. Not used for gameplay.");
        if (!isActive)
            return ("Manually retired", "Deactivated by admin or migration. Not picked for new sessions.");
        if (isLegacy)
            return ("Legacy active", "Old-format slot still marked active. Puzzle content still comes from runtime generator.");
        return ("Runtime slot", "Modern rotation slot. ID used for seeding; puzzle generated at runtime.");
    }

    public async Task<LevelListResult> ListAsync(string? timeMode, string? puzzleTier, bool? isActive, int page, int pageSize, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminListLevels", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TimeMode", string.IsNullOrWhiteSpace(timeMode) ? DBNull.Value : timeMode.Trim());
        cmd.Parameters.AddWithValue("@PuzzleTier", string.IsNullOrWhiteSpace(puzzleTier) ? DBNull.Value : puzzleTier.Trim());
        cmd.Parameters.AddWithValue("@IsActive", isActive.HasValue ? isActive.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@Page", page < 1 ? 1 : page);
        cmd.Parameters.AddWithValue("@PageSize", pageSize < 1 ? 50 : pageSize);

        var totalCount = 0;
        var items = new List<LevelRowDto>();

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (await rdr.ReadAsync(ct))
            totalCount = (int)rdr["TotalCount"];

        if (await rdr.NextResultAsync(ct))
        {
            while (await rdr.ReadAsync(ct))
            {
                var rowIsActive = (bool)rdr["IsActive"];
                var arrowCount = (int)rdr["ArrowCount"];
                var gridCols = (int)rdr["GridCols"];
                var (slotType, statusNote) = DescribeSlot(rowIsActive, arrowCount, gridCols);
                items.Add(new LevelRowDto(
                    (int)rdr["LevelId"],
                    rdr["TimeMode"]?.ToString() ?? "",
                    rdr["PuzzleTier"]?.ToString() ?? "",
                    (int)rdr["DifficultyScore"],
                    arrowCount,
                    gridCols,
                    (int)rdr["GridRows"],
                    (int)rdr["Seed"],
                    rowIsActive,
                    (DateTime)rdr["CreatedAt"],
                    slotType,
                    statusNote));
            }
        }

        return new LevelListResult(totalCount, items);
    }

    public async Task<ApiResponse<object>> PurgeInactiveAsync(bool legacyOnly = true, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminPurgeInactiveLevels", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@LegacyOnly", legacyOnly);
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 500);
        messageParam.Direction = ParameterDirection.Output;
        var deletedParam = cmd.Parameters.Add("@Deleted", SqlDbType.Int);
        deletedParam.Direction = ParameterDirection.Output;
        var skippedParam = cmd.Parameters.Add("@Skipped", SqlDbType.Int);
        skippedParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);

        var message = messageParam.Value?.ToString() ?? "";
        var deleted = deletedParam.Value == DBNull.Value ? 0 : (int)deletedParam.Value;
        var skipped = skippedParam.Value == DBNull.Value ? 0 : (int)skippedParam.Value;
        return new ApiResponse<object>(true, message, new { deleted, skipped, legacyOnly });
    }

    public async Task<ApiResponse<object>> ExpandPoolAsync(int targetTotal, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminExpandLevelPool", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TargetTotal", targetTotal);
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        messageParam.Direction = ParameterDirection.Output;
        var addedParam = cmd.Parameters.Add("@Added", SqlDbType.Int);
        addedParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);

        var code = (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        var added = addedParam.Value == DBNull.Value ? 0 : (int)addedParam.Value;
        return code == 0
            ? new ApiResponse<object>(true, message, new { added, targetTotal })
            : new ApiResponse<object>(false, message);
    }

    public async Task<ApiResponse<object>> SetActiveAsync(int levelId, bool isActive, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminSetLevelActive", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@LevelId", levelId);
        cmd.Parameters.AddWithValue("@IsActive", isActive);
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        messageParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);

        var code = (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        return code == 0
            ? new ApiResponse<object>(true, message)
            : new ApiResponse<object>(false, message);
    }
}
