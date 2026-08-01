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

    public LevelService(ISqlConnectionFactory db) => _db = db;

    public async Task<AllocatedLevelResult?> AllocateLevelAsync(Guid playerId, AllocateLevelRequest req)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_AllocateLevel", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@TimeMode", req.TimeMode);
        cmd.Parameters.AddWithValue("@RewardMode", req.RewardMode);

        var pId = cmd.Parameters.Add("@LevelId", SqlDbType.Int);
        pId.Direction = ParameterDirection.Output;
        var pGrid = cmd.Parameters.Add("@GridJson", SqlDbType.NVarChar, -1);
        pGrid.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        if (pId.Value == DBNull.Value) return null;

        return new AllocatedLevelResult((int)pId.Value, (string)pGrid.Value!);
    }
}
