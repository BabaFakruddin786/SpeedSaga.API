using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface ITickerConfigService
{
    Task<TickerConfigDto?> GetPublicConfigAsync();
    Task<ApiResponse<object>> UpdateConfigAsync(UpdateTickerConfigRequest req);
}

public record TickerConfigDto(
    bool IsEnabled,
    int RotateSeconds,
    string[] Messages,
    DateTime UpdatedAt);

public record UpdateTickerConfigRequest(
    bool IsEnabled,
    int RotateSeconds,
    string[]? Messages);

public class TickerConfigService : ITickerConfigService
{
    const string CacheKey = "app_ticker_config";
    static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    static readonly string[] DefaultMessages =
    {
        "Tournament starts in 2 hours! Join now",
        "New entry fee bracket Rs 750 added",
        "Complete 5 levels, earn bonus Rs 200"
    };

    readonly ISqlConnectionFactory _db;
    readonly IMemoryCache _cache;

    public TickerConfigService(ISqlConnectionFactory db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<TickerConfigDto?> GetPublicConfigAsync()
    {
        if (_cache.TryGetValue(CacheKey, out TickerConfigDto? cached) && cached != null)
            return cached;

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetAppTickerConfig", cn) { CommandType = CommandType.StoredProcedure };
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync())
            return null;

        var dto = MapRow(rdr);
        _cache.Set(CacheKey, dto, CacheTtl);
        return dto;
    }

    public async Task<ApiResponse<object>> UpdateConfigAsync(UpdateTickerConfigRequest req)
    {
        var messages = req.Messages?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Take(12)
            .ToArray() ?? Array.Empty<string>();

        if (messages.Length == 0)
            return new ApiResponse<object>(false, "At least one ticker message is required.");

        var rotateSeconds = req.RotateSeconds is >= 2 and <= 30 ? req.RotateSeconds : 3;
        var messagesJson = JsonSerializer.Serialize(messages);

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_UpdateAppTickerConfig", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@IsEnabled", req.IsEnabled);
        cmd.Parameters.AddWithValue("@RotateSeconds", rotateSeconds);
        cmd.Parameters.AddWithValue("@MessagesJson", messagesJson);
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        messageParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        var code = (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        if (code != 0)
            return new ApiResponse<object>(false, message);

        _cache.Remove(CacheKey);
        var updated = await GetPublicConfigAsync();
        return new ApiResponse<object>(true, message, updated);
    }

    static TickerConfigDto MapRow(SqlDataReader rdr)
    {
        var isEnabled = rdr["IsEnabled"] is bool enabled && enabled;
        var rotateSeconds = rdr["RotateSeconds"] is int secs && secs >= 2 && secs <= 30 ? secs : 3;
        var messages = ParseJsonArray(rdr["MessagesJson"].ToString(), DefaultMessages);
        var updatedAt = rdr["UpdatedAt"] is DateTime dt ? dt : DateTime.UtcNow;
        return new TickerConfigDto(isEnabled, rotateSeconds, messages, updatedAt);
    }

    static string[] ParseJsonArray(string? json, string[] fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(json);
            if (parsed == null || parsed.Length == 0) return fallback;
            return parsed.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
        }
        catch
        {
            return fallback;
        }
    }
}
