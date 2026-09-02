using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IAdminPlayerService
{
    Task<IReadOnlyList<object>> SearchAsync(string? query, int page, CancellationToken ct = default);
    Task<object?> GetDetailAsync(Guid playerId, CancellationToken ct = default);
    Task<ApiResponse<object>> SetBanAsync(Guid playerId, bool isBanned, string? reason, CancellationToken ct = default);
}

public class AdminPlayerService : IAdminPlayerService
{
    readonly ISqlConnectionFactory _db;

    public AdminPlayerService(ISqlConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<object>> SearchAsync(string? query, int page, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminSearchPlayers", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Query", string.IsNullOrWhiteSpace(query) ? DBNull.Value : query.Trim());
        cmd.Parameters.AddWithValue("@PageNo", page < 1 ? 1 : page);
        cmd.Parameters.AddWithValue("@PageSize", 50);
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
                stateCode = rdr["StateCode"]?.ToString(),
                isActive = (bool)rdr["IsActive"],
                isBanned = (bool)rdr["IsBanned"],
                createdAt = (DateTime)rdr["CreatedAt"],
                lastLoginAt = rdr["LastLoginAt"] == DBNull.Value ? (DateTime?)null : (DateTime)rdr["LastLoginAt"],
                balancePaise = rdr["BalancePaise"] == DBNull.Value ? 0L : (long)rdr["BalancePaise"],
                depositPaise = rdr["DepositPaise"] == DBNull.Value ? 0L : (long)rdr["DepositPaise"],
                withdrawnPaise = rdr["WithdrawnPaise"] == DBNull.Value ? 0L : (long)rdr["WithdrawnPaise"],
                isFullyVerified = rdr["IsFullyVerified"] != DBNull.Value && (bool)rdr["IsFullyVerified"]
            });
        }
        return list;
    }

    public async Task<object?> GetDetailAsync(Guid playerId, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminGetPlayerDetail", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return null;
        return new
        {
            playerId = rdr["PlayerId"].ToString(),
            username = rdr["Username"]?.ToString(),
            contactEmail = rdr["ContactEmail"]?.ToString(),
            contactPhone = rdr["ContactPhone"]?.ToString(),
            stateCode = rdr["StateCode"]?.ToString(),
            referralCode = rdr["ReferralCode"]?.ToString(),
            isActive = (bool)rdr["IsActive"],
            isBanned = (bool)rdr["IsBanned"],
            bannedReason = rdr["BannedReason"]?.ToString(),
            createdAt = (DateTime)rdr["CreatedAt"],
            lastLoginAt = rdr["LastLoginAt"] == DBNull.Value ? (DateTime?)null : (DateTime)rdr["LastLoginAt"],
            balancePaise = rdr["BalancePaise"] == DBNull.Value ? 0L : (long)rdr["BalancePaise"],
            depositPaise = rdr["DepositPaise"] == DBNull.Value ? 0L : (long)rdr["DepositPaise"],
            winningPaise = rdr["WinningPaise"] == DBNull.Value ? 0L : (long)rdr["WinningPaise"],
            withdrawnPaise = rdr["WithdrawnPaise"] == DBNull.Value ? 0L : (long)rdr["WithdrawnPaise"],
            bonusPaise = rdr["BonusPaise"] == DBNull.Value ? 0L : (long)rdr["BonusPaise"],
            totalGames = rdr["TotalGames"] == DBNull.Value ? 0 : (int)rdr["TotalGames"],
            totalWins = rdr["TotalWins"] == DBNull.Value ? 0 : (int)rdr["TotalWins"],
            totalLosses = rdr["TotalLosses"] == DBNull.Value ? 0 : (int)rdr["TotalLosses"],
            winRatePct = rdr["WinRatePct"] == DBNull.Value ? 0m : (decimal)rdr["WinRatePct"],
            aadhaarStatus = rdr["AadhaarStatus"]?.ToString(),
            panStatus = rdr["PANStatus"]?.ToString(),
            bankStatus = rdr["BankStatus"]?.ToString(),
            isFullyVerified = rdr["IsFullyVerified"] != DBNull.Value && (bool)rdr["IsFullyVerified"]
        };
    }

    public async Task<ApiResponse<object>> SetBanAsync(Guid playerId, bool isBanned, string? reason, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminSetPlayerBan", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@IsBanned", isBanned);
        cmd.Parameters.AddWithValue("@BannedReason", (object?)reason?.Trim() ?? DBNull.Value);
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
