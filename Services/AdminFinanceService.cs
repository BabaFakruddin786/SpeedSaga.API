using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;

namespace SpeedSaga.API.Services;

public interface IAdminFinanceService
{
    Task<IReadOnlyList<object>> ListTransactionsAsync(string? txnType, Guid? playerId, DateTime? from, DateTime? to, int page, CancellationToken ct = default);
    Task<IReadOnlyList<object>> TopDepositorsAsync(int days, int topN, CancellationToken ct = default);
    Task<IReadOnlyList<object>> PlayerFinanceDailyAsync(Guid playerId, int days, CancellationToken ct = default);
}

public class AdminFinanceService : IAdminFinanceService
{
    readonly ISqlConnectionFactory _db;

    public AdminFinanceService(ISqlConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<object>> ListTransactionsAsync(string? txnType, Guid? playerId, DateTime? from, DateTime? to, int page, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminListTransactions", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TxnType", string.IsNullOrWhiteSpace(txnType) ? DBNull.Value : txnType.Trim());
        cmd.Parameters.AddWithValue("@PlayerId", playerId.HasValue ? playerId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@FromDate", from.HasValue ? from.Value.Date : DBNull.Value);
        cmd.Parameters.AddWithValue("@ToDate", to.HasValue ? to.Value.Date : DBNull.Value);
        cmd.Parameters.AddWithValue("@PageNo", page < 1 ? 1 : page);
        cmd.Parameters.AddWithValue("@PageSize", 50);
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            list.Add(new
            {
                txnId = rdr["TxnId"].ToString(),
                playerId = rdr["PlayerId"].ToString(),
                username = rdr["Username"]?.ToString(),
                contactEmail = rdr["ContactEmail"]?.ToString(),
                contactPhone = rdr["ContactPhone"]?.ToString(),
                txnType = rdr["TxnType"].ToString(),
                amountPaise = (long)rdr["AmountPaise"],
                balanceAfter = (long)rdr["BalanceAfter"],
                status = rdr["Status"].ToString(),
                gateway = rdr["Gateway"]?.ToString(),
                gatewayRef = rdr["GatewayRef"]?.ToString(),
                remarks = rdr["Remarks"]?.ToString(),
                createdAt = (DateTime)rdr["CreatedAt"]
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<object>> TopDepositorsAsync(int days, int topN, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminTopDepositors", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Days", days < 1 ? 30 : Math.Min(days, 365));
        cmd.Parameters.AddWithValue("@TopN", topN < 1 ? 10 : Math.Min(topN, 50));
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            list.Add(new
            {
                playerId = rdr["PlayerId"].ToString(),
                username = rdr["Username"]?.ToString(),
                contactEmail = rdr["ContactEmail"]?.ToString(),
                contactPhone = rdr["ContactPhone"]?.ToString(),
                totalDepositsPaise = (long)rdr["TotalDepositsPaise"],
                depositCount = (int)rdr["DepositCount"],
                lastDepositAt = rdr["LastDepositAt"] == DBNull.Value ? (DateTime?)null : (DateTime)rdr["LastDepositAt"]
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<object>> PlayerFinanceDailyAsync(Guid playerId, int days, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminPlayerFinanceDaily", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
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
                rewardsPaise = (long)rdr["RewardsPaise"]
            });
        }
        return list;
    }
}
