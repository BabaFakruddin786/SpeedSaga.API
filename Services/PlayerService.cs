using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;

namespace SpeedSaga.API.Services;

public interface IPlayerService
{
    Task<object?> GetDashboardAsync(Guid playerId);
}

public class PlayerService : IPlayerService
{
    private readonly ISqlConnectionFactory _db;

    public PlayerService(ISqlConnectionFactory db) => _db = db;

    public async Task<object?> GetDashboardAsync(Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetPlayerDashboard", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;

        return new
        {
            PlayerId = rdr["PlayerId"].ToString(),
            ContactEmail = rdr["ContactEmail"]?.ToString(),
            ContactPhone = rdr["ContactPhone"]?.ToString(),
            Username = rdr["Username"]?.ToString(),
            StateCode = rdr["StateCode"]?.ToString(),
            BalanceRs = (long)rdr["BalancePaise"] / 100.0,
            BalancePaise = (long)rdr["BalancePaise"],
            DepositPaise = (long)rdr["DepositPaise"],
            WinningPaise = (long)rdr["WinningPaise"],
            WithdrawnPaise = (long)rdr["WithdrawnPaise"],
            TotalGames = (int)rdr["TotalGames"],
            TotalWins = (int)rdr["TotalWins"],
            TotalLosses = (int)rdr["TotalLosses"],
            WinRatePct = (decimal)rdr["WinRatePct"],
            CurrentStreak = (int)rdr["CurrentStreak"],
            BestStreak = (int)rdr["BestStreak"],
            TotalEntryPaise = (long)rdr["TotalEntryPaise"],
            TotalRewardPaise = (long)rdr["TotalRewardPaise"],
            AadhaarStatus = rdr["AadhaarStatus"].ToString(),
            PANStatus = rdr["PANStatus"].ToString(),
            BankStatus = rdr["BankStatus"].ToString(),
            IsFullyVerified = (bool)rdr["IsFullyVerified"]
        };
    }
}
