using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IAdminNotificationService
{
    Task<IReadOnlyList<object>> ListAsync(int page, CancellationToken ct = default);
    Task<ApiResponse<object>> BroadcastAsync(string title, string body, string? notifType, CancellationToken ct = default);
    Task<ApiResponse<object>> SendToPlayerAsync(Guid playerId, string title, string body, string? notifType, CancellationToken ct = default);
}

public interface IFcmPushService
{
    Task TryPushToPlayerAsync(Guid playerId, string title, string body, CancellationToken ct = default);
}

public class AdminNotificationService : IAdminNotificationService
{
    readonly ISqlConnectionFactory _db;
    readonly IFcmPushService _fcm;

    public AdminNotificationService(ISqlConnectionFactory db, IFcmPushService fcm)
    {
        _db = db;
        _fcm = fcm;
    }

    public async Task<IReadOnlyList<object>> ListAsync(int page, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminListNotifications", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PageNo", page < 1 ? 1 : page);
        cmd.Parameters.AddWithValue("@PageSize", 50);
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            list.Add(new
            {
                notifId = rdr["NotifId"].ToString(),
                playerId = rdr["PlayerId"]?.ToString(),
                username = rdr["Username"]?.ToString(),
                contactPhone = rdr["ContactPhone"]?.ToString(),
                contactEmail = rdr["ContactEmail"]?.ToString(),
                title = rdr["Title"].ToString(),
                body = rdr["Body"].ToString(),
                notifType = rdr["NotifType"].ToString(),
                fcmSent = (bool)rdr["FCMSent"],
                audience = rdr["Audience"].ToString(),
                createdAt = (DateTime)rdr["CreatedAt"]
            });
        }
        return list;
    }

    public async Task<ApiResponse<object>> BroadcastAsync(string title, string body, string? notifType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            return new ApiResponse<object>(false, "Title and body are required.");

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminBroadcastNotification", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Title", title.Trim());
        cmd.Parameters.AddWithValue("@Body", body.Trim());
        cmd.Parameters.AddWithValue("@NotifType", string.IsNullOrWhiteSpace(notifType) ? "System" : notifType.Trim());
        var notifIdParam = cmd.Parameters.Add("@NotifId", SqlDbType.UniqueIdentifier);
        notifIdParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);
        return new ApiResponse<object>(true, "Broadcast notification sent.", new { notifId = notifIdParam.Value?.ToString() });
    }

    public async Task<ApiResponse<object>> SendToPlayerAsync(Guid playerId, string title, string body, string? notifType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            return new ApiResponse<object>(false, "Title and body are required.");

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminSendPlayerNotification", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@Title", title.Trim());
        cmd.Parameters.AddWithValue("@Body", body.Trim());
        cmd.Parameters.AddWithValue("@NotifType", string.IsNullOrWhiteSpace(notifType) ? "System" : notifType.Trim());
        var notifIdParam = cmd.Parameters.Add("@NotifId", SqlDbType.UniqueIdentifier);
        notifIdParam.Direction = ParameterDirection.Output;
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        messageParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);

        var code = (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        if (code != 0)
            return new ApiResponse<object>(false, message);

        await _fcm.TryPushToPlayerAsync(playerId, title.Trim(), body.Trim(), ct);
        return new ApiResponse<object>(true, message, new { notifId = notifIdParam.Value?.ToString() });
    }
}

public class FcmPushService : IFcmPushService
{
    readonly ISqlConnectionFactory _db;
    readonly IPaymentConfigService _paymentConfig;
    readonly IHttpClientFactory _httpClientFactory;
    readonly ILogger<FcmPushService> _logger;

    public FcmPushService(
        ISqlConnectionFactory db,
        IPaymentConfigService paymentConfig,
        IHttpClientFactory httpClientFactory,
        ILogger<FcmPushService> logger)
    {
        _db = db;
        _paymentConfig = paymentConfig;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task TryPushToPlayerAsync(Guid playerId, string title, string body, CancellationToken ct = default)
    {
        var secrets = await _paymentConfig.GetSecretsAsync(ct);
        if (!secrets.IsPushEnabled || string.IsNullOrWhiteSpace(secrets.FcmServerKey))
            return;

        var tokens = await GetDeviceTokensAsync(playerId, ct);
        if (tokens.Count == 0) return;

        var client = _httpClientFactory.CreateClient("Fcm");
        foreach (var token in tokens)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    to = token,
                    notification = new { title, body }
                });
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/fcm/send");
                request.Headers.TryAddWithoutValidation("Authorization", $"key={secrets.FcmServerKey}");
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("FCM push failed for player {PlayerId}: {Status}", playerId, response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM push error for player {PlayerId}", playerId);
            }
        }
    }

    async Task<IReadOnlyList<string>> GetDeviceTokensAsync(Guid playerId, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT DeviceToken FROM PlayerDeviceTokens WHERE PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        var list = new List<string>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
            list.Add(rdr["DeviceToken"].ToString() ?? "");
        return list;
    }
}
