using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;

namespace SpeedSaga.API.Services;

public interface INotificationService
{
    Task<object?> GetNotificationsAsync(Guid playerId, int page);
    Task MarkReadAsync(Guid playerId, Guid notifId);
}

public class NotificationService : INotificationService
{
    private readonly ISqlConnectionFactory _db;

    public NotificationService(ISqlConnectionFactory db) => _db = db;

    public async Task<object?> GetNotificationsAsync(Guid playerId, int page)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetNotifications", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@PageNo", page);
        cmd.Parameters.AddWithValue("@PageSize", 20);

        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new
            {
                NotifId = rdr["NotifId"].ToString(),
                Title = rdr["Title"].ToString(),
                Body = rdr["Body"].ToString(),
                NotifType = rdr["NotifType"].ToString(),
                IsRead = (bool)rdr["IsRead"],
                CreatedAt = (DateTime)rdr["CreatedAt"]
            });
        }

        return list;
    }

    public async Task MarkReadAsync(Guid playerId, Guid notifId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_MarkNotificationRead", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@NotifId", notifId);
        await cmd.ExecuteNonQueryAsync();
    }
}
