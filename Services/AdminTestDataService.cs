using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public record TestDataCountsDto(
    int PlayerCount,
    int TransactionCount,
    int GameSessionCount,
    int SupportTicketCount,
    int NotificationCount,
    int TournamentEntryCount);

public interface IAdminTestDataService
{
    bool IsEnabled { get; }
    Task<TestDataCountsDto?> GetCountsAsync(CancellationToken ct = default);
    Task<ApiResponse<object>> DeletePlayerAsync(Guid playerId, CancellationToken ct = default);
    Task<ApiResponse<object>> PurgeAllAsync(CancellationToken ct = default);
}

public class AdminTestDataService : IAdminTestDataService
{
    readonly ISqlConnectionFactory _db;
    readonly AdminOptions _admin;

    public AdminTestDataService(ISqlConnectionFactory db, Microsoft.Extensions.Options.IOptions<AdminOptions> admin)
    {
        _db = db;
        _admin = admin.Value;
    }

    public bool IsEnabled => _admin.AllowTestDataPurge;

    public async Task<TestDataCountsDto?> GetCountsAsync(CancellationToken ct = default)
    {
        if (!IsEnabled) return null;

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminGetTestDataCounts", cn) { CommandType = CommandType.StoredProcedure };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return null;

        return new TestDataCountsDto(
            (int)rdr["PlayerCount"],
            (int)rdr["TransactionCount"],
            (int)rdr["GameSessionCount"],
            (int)rdr["SupportTicketCount"],
            (int)rdr["NotificationCount"],
            (int)rdr["TournamentEntryCount"]);
    }

    public async Task<ApiResponse<object>> DeletePlayerAsync(Guid playerId, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return new ApiResponse<object>(false, "Test data purge is disabled on this server.");

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminDeletePlayer", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 300);
        messageParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);

        var result = resultParam.Value == DBNull.Value ? 0 : (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        return result == 0
            ? new ApiResponse<object>(true, message)
            : new ApiResponse<object>(false, message);
    }

    public async Task<ApiResponse<object>> PurgeAllAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
            return new ApiResponse<object>(false, "Test data purge is disabled on this server.");

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminPurgeAllPlayerData", cn) { CommandType = CommandType.StoredProcedure };
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 300);
        messageParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);

        var result = resultParam.Value == DBNull.Value ? 0 : (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        return result == 0
            ? new ApiResponse<object>(true, message)
            : new ApiResponse<object>(false, message);
    }
}
