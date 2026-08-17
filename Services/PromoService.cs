using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public record PromoOffer(string Code, string Title, string Description, long BonusPaise, bool Eligible, bool Claimed);

public interface IPromoService
{
    Task<IReadOnlyList<PromoOffer>> GetOffersAsync(Guid playerId);
    Task<ApiResponse<object>> ClaimAsync(Guid playerId, string promoCode);
}

public class PromoService : IPromoService
{
    readonly ISqlConnectionFactory _db;

    public PromoService(ISqlConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<PromoOffer>> GetOffersAsync(Guid playerId)
    {
        var stats = await LoadStatsAsync(playerId);
        var claimed = await LoadClaimedAsync(playerId);
        var offers = new List<PromoOffer>
        {
            Build("DAILY_LOGIN", "Daily login bonus", "Rs 25 free credit once per day", 2500,
                stats.LastLoginEligible, claimed),
            Build("FIRST_WIN", "First win bonus", "Win your first paid game", 5000,
                stats.TotalWins >= 1, claimed),
            Build("WIN_STREAK_3", "3-win streak", "Win 3 games in a row", 5000,
                stats.CurrentStreak >= 3, claimed),
            Build("PRACTICE_5", "Practice champion", "Complete 5 practice games", 2000,
                stats.PracticeGames >= 5, claimed)
        };
        return offers;
    }

    static PromoOffer Build(string code, string title, string desc, long paise, bool eligible, HashSet<string> claimed)
        => new(code, title, desc, paise, eligible && !claimed.Contains(code), claimed.Contains(code));

    public async Task<ApiResponse<object>> ClaimAsync(Guid playerId, string promoCode)
    {
        promoCode = promoCode?.Trim().ToUpperInvariant() ?? "";
        var offers = await GetOffersAsync(playerId);
        var offer = offers.FirstOrDefault(o => o.Code == promoCode);
        if (offer == null) return new ApiResponse<object>(false, "Unknown offer");
        if (offer.Claimed) return new ApiResponse<object>(false, "Already claimed");
        if (!offer.Eligible) return new ApiResponse<object>(false, "Requirements not met yet");

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_ClaimPromo", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@PromoCode", promoCode);
        cmd.Parameters.AddWithValue("@BonusPaise", offer.BonusPaise);
        var pRes = cmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;
        var pMsg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        var result = (int)pRes.Value!;
        var msg = (string)pMsg.Value!;
        return result == 1
            ? new ApiResponse<object>(true, msg, new { bonusRs = offer.BonusPaise / 100.0 })
            : new ApiResponse<object>(false, msg);
    }

    async Task<(int TotalWins, int CurrentStreak, int PracticeGames, bool LastLoginEligible)> LoadStatsAsync(Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT S.TotalWins, S.CurrentStreak,
                (SELECT COUNT(*) FROM GameSessions WHERE Player1Id = @PlayerId AND GameMode = 'FreePlay' AND Status = 'Complete') AS PracticeGames
            FROM PlayerStats S WHERE S.PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return (0, 0, 0, false);

        var wins = (int)rdr["TotalWins"];
        var streak = (int)rdr["CurrentStreak"];
        var practice = (int)rdr["PracticeGames"];

        var dailyEligible = !await IsClaimedTodayAsync(playerId, "DAILY_LOGIN");
        return (wins, streak, practice, dailyEligible);
    }

    async Task<bool> IsClaimedTodayAsync(Guid playerId, string code)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT 1 FROM PlayerPromoClaims WHERE PlayerId = @PlayerId AND PromoCode = @Code AND ClaimedAt >= CAST(GETDATE() AS DATE)", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@Code", code);
        return await cmd.ExecuteScalarAsync() != null;
    }

    async Task<HashSet<string>> LoadClaimedAsync(Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT PromoCode FROM PlayerPromoClaims WHERE PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) set.Add(rdr["PromoCode"].ToString()!);
        return set;
    }
}
