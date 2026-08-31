using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;

namespace SpeedSaga.API.Services;

public interface IAdminDashboardService
{
    Task<object?> GetStatsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<object>> GetDailyFlowAsync(int days, CancellationToken ct = default);
    Task<IReadOnlyList<object>> GetFlowByTypeAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
}

public class AdminDashboardService : IAdminDashboardService
{
    readonly ISqlConnectionFactory _db;

    public AdminDashboardService(ISqlConnectionFactory db) => _db = db;

    public async Task<object?> GetStatsAsync(CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminDashboardStats", cn) { CommandType = CommandType.StoredProcedure };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return null;
        return new
        {
            totalPlayers = (int)rdr["TotalPlayers"],
            activePlayers7d = (int)rdr["ActivePlayers7d"],
            totalWalletBalancePaise = (long)rdr["TotalWalletBalancePaise"],
            totalDepositedAllTimePaise = (long)rdr["TotalDepositedAllTimePaise"],
            totalWithdrawnAllTimePaise = (long)rdr["TotalWithdrawnAllTimePaise"],
            depositsTodayPaise = (long)rdr["DepositsTodayPaise"],
            withdrawalsTodayPaise = (long)rdr["WithdrawalsTodayPaise"],
            entryFeesTodayPaise = (long)rdr["EntryFeesTodayPaise"],
            rewardsTodayPaise = (long)rdr["RewardsTodayPaise"],
            pendingKycReviews = (int)rdr["PendingKycReviews"],
            openSupportTickets = (int)rdr["OpenSupportTickets"],
            depositCountAllTime = (int)rdr["DepositCountAllTime"],
            withdrawalCountAllTime = (int)rdr["WithdrawalCountAllTime"]
        };
    }

    public async Task<IReadOnlyList<object>> GetDailyFlowAsync(int days, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminFinanceDailyFlow", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Days", days < 1 ? 30 : Math.Min(days, 365));
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            list.Add(new
            {
                date = ((DateTime)rdr["Date"]).ToString("yyyy-MM-dd"),
                depositsPaise = (long)rdr["DepositsPaise"],
                withdrawalsPaise = (long)rdr["WithdrawalsPaise"],
                entryFeesPaise = (long)rdr["EntryFeesPaise"],
                rewardsPaise = (long)rdr["RewardsPaise"],
                netDepositsPaise = (long)rdr["NetDepositsPaise"]
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<object>> GetFlowByTypeAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminFinanceByType", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@FromDate", from.HasValue ? from.Value.Date : DBNull.Value);
        cmd.Parameters.AddWithValue("@ToDate", to.HasValue ? to.Value.Date : DBNull.Value);
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            list.Add(new
            {
                txnType = rdr["TxnType"].ToString(),
                txnCount = (int)rdr["TxnCount"],
                totalPaise = (long)rdr["TotalPaise"]
            });
        }
        return list;
    }
}
