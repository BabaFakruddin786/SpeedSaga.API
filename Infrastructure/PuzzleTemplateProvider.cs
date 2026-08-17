using System.Text.Json;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Infrastructure;

public static class PuzzleTemplateProvider
{
    public static bool IsTooSimple(string? gridJson) => !IsValidGridJson(gridJson);

    public static bool IsValidGridJson(string? gridJson)
    {
        if (string.IsNullOrWhiteSpace(gridJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(gridJson);
            if (!doc.RootElement.TryGetProperty("arrows", out var arr)) return false;
            if (arr.GetArrayLength() < ComplexPuzzleGenerator.MinArrows) return false;

            int valid = 0;
            foreach (var arrow in arr.EnumerateArray())
            {
                if (!arrow.TryGetProperty("pts", out var pts) || pts.ValueKind != JsonValueKind.Array) continue;
                if (pts.GetArrayLength() < 2) continue;
                valid++;
            }
            return valid >= ComplexPuzzleGenerator.MinArrows * 0.85;
        }
        catch { return false; }
    }

    /// <summary>Deterministic seed shared by all players for the same level slot — enables warmup cache hits.</summary>
    public static int PuzzleSeed(int levelId, string tier)
        => (levelId * 7919) + TierSeedOffset(tier);

    static int TierSeedOffset(string tier) => tier switch
    {
        "SuperHard" => 4,
        "Hard" => 3,
        "Medium" => 2,
        _ => 1
    };

    public static AllocatedLevelResult Generate(string tier, int levelId, Guid playerId)
    {
        int seed = PuzzleSeed(levelId, tier);
        var json = ComplexPuzzleGenerator.ToJson(tier, seed);
        if (IsValidGridJson(json))
            return new AllocatedLevelResult(levelId, json, tier, ComplexPuzzleGenerator.TargetForTier(tier));

        json = ComplexPuzzleGenerator.ToJson(tier, seed + 1337);
        return new AllocatedLevelResult(levelId, json, tier, ComplexPuzzleGenerator.TargetForTier(tier));
    }

    public static AllocatedLevelResult Enrich(AllocatedLevelResult level, string tier, Guid playerId)
    {
        if (IsValidGridJson(level.GridJson)) return level;
        return Generate(tier, level.LevelId, playerId);
    }
}
