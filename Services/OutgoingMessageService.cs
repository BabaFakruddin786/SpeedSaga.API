using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;

namespace SpeedSaga.API.Services;

public interface IOutgoingMessageService
{
    Task<IReadOnlyList<object>> GetPlayerMessagesAsync(Guid playerId, int page, CancellationToken ct = default);
}

public class OutgoingMessageService : IOutgoingMessageService
{
    readonly ISqlConnectionFactory _db;

    public OutgoingMessageService(ISqlConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<object>> GetPlayerMessagesAsync(Guid playerId, int page, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_GetPlayerOutgoingMessages", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@PageNo", page < 1 ? 1 : page);
        cmd.Parameters.AddWithValue("@PageSize", 20);

        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            list.Add(new
            {
                MessageId = rdr["MessageId"].ToString(),
                Channel = rdr["Channel"].ToString(),
                Purpose = rdr["Purpose"].ToString(),
                DestinationMask = rdr["DestinationMask"].ToString(),
                BodyPreview = rdr["BodyPreview"].ToString(),
                Provider = rdr["Provider"].ToString(),
                Status = rdr["Status"].ToString(),
                StatusDetail = rdr["StatusDetail"] == DBNull.Value ? null : rdr["StatusDetail"].ToString(),
                CreatedAt = (DateTime)rdr["CreatedAt"],
                SentAt = rdr["SentAt"] == DBNull.Value ? (DateTime?)null : (DateTime)rdr["SentAt"],
                DeliveredAt = rdr["DeliveredAt"] == DBNull.Value ? (DateTime?)null : (DateTime)rdr["DeliveredAt"]
            });
        }
        return list;
    }
}
