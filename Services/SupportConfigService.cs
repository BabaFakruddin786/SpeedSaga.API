using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface ISupportConfigService
{
    Task<SupportConfigDto?> GetPublicConfigAsync();
    Task<ApiResponse<object>> UpdateConfigAsync(UpdateSupportConfigRequest req);
}

public record SupportConfigDto(
    string Email,
    string PhoneDisplay,
    string PhoneTel,
    string WhatsAppDigits,
    string HoursLine,
    string[] HoursBullets,
    string[] FaqBullets,
    DateTime UpdatedAt);

public record UpdateSupportConfigRequest(
    string Email,
    string Phone,
    string WhatsApp,
    string HoursLine,
    string[]? HoursBullets,
    string[]? FaqBullets);

public class SupportConfigService : ISupportConfigService
{
    const string CacheKey = "app_support_config";
    static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    readonly ISqlConnectionFactory _db;
    readonly IMemoryCache _cache;

    static readonly string[] DefaultHoursBullets =
    {
        "Mon–Sat · 10:00 AM – 7:00 PM IST",
        "Sunday & public holidays · email only",
        "WhatsApp usually replies within 2 hours on business days"
    };

    static readonly string[] DefaultFaqBullets =
    {
        "Withdrawal pending? Complete Account Verification first.",
        "Deposit missing? Wait 10 minutes, then email us with payment ID.",
        "Game disconnected? Two-player forfeit rules apply after timeout.",
        "Forgot password? Use Forgot Password on the login screen."
    };

    public SupportConfigService(ISqlConnectionFactory db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<SupportConfigDto?> GetPublicConfigAsync()
    {
        if (_cache.TryGetValue(CacheKey, out SupportConfigDto? cached) && cached != null)
            return cached;

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetAppSupportConfig", cn) { CommandType = CommandType.StoredProcedure };
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync())
            return null;

        var dto = MapRow(rdr);
        _cache.Set(CacheKey, dto, CacheTtl);
        return dto;
    }

    public async Task<ApiResponse<object>> UpdateConfigAsync(UpdateSupportConfigRequest req)
    {
        var email = req.Email?.Trim() ?? "";
        var phone = NormalizeDigits(req.Phone);
        var whatsApp = NormalizeDigits(req.WhatsApp);
        var hoursLine = string.IsNullOrWhiteSpace(req.HoursLine) ? "Mon–Sat 10 AM – 7 PM IST" : req.HoursLine.Trim();
        var hoursJson = JsonSerializer.Serialize(req.HoursBullets?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray() ?? DefaultHoursBullets);
        var faqJson = JsonSerializer.Serialize(req.FaqBullets?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray() ?? DefaultFaqBullets);

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_UpdateAppSupportConfig", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@SupportEmail", email);
        cmd.Parameters.AddWithValue("@SupportPhone", phone);
        cmd.Parameters.AddWithValue("@SupportWhatsApp", whatsApp);
        cmd.Parameters.AddWithValue("@SupportHoursLine", hoursLine);
        cmd.Parameters.AddWithValue("@SupportHoursJson", hoursJson);
        cmd.Parameters.AddWithValue("@SupportFaqJson", faqJson);
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

    static SupportConfigDto MapRow(SqlDataReader rdr)
    {
        var email = rdr["SupportEmail"].ToString() ?? "support@speedsaga.com";
        var phone = NormalizeDigits(rdr["SupportPhone"].ToString());
        var whatsApp = NormalizeDigits(rdr["SupportWhatsApp"].ToString());
        var hoursLine = rdr["SupportHoursLine"].ToString() ?? "Mon–Sat 10 AM – 7 PM IST";
        var hoursBullets = ParseJsonArray(rdr["SupportHoursJson"].ToString(), DefaultHoursBullets);
        var faqBullets = ParseJsonArray(rdr["SupportFaqJson"].ToString(), DefaultFaqBullets);
        var updatedAt = rdr["UpdatedAt"] is DateTime dt ? dt : DateTime.UtcNow;

        return new SupportConfigDto(
            email,
            FormatPhoneDisplay(phone),
            FormatPhoneTel(phone),
            FormatWhatsAppDigits(whatsApp),
            hoursLine,
            hoursBullets,
            faqBullets,
            updatedAt);
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

    static string NormalizeDigits(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : Regex.Replace(value, @"\D", "");

    static string FormatPhoneTel(string digits)
    {
        if (string.IsNullOrEmpty(digits)) return "+919052916052";
        if (digits.StartsWith("91") && digits.Length >= 12) return "+" + digits;
        if (digits.Length == 10) return "+91" + digits;
        return digits.StartsWith("+") ? digits : "+" + digits;
    }

    static string FormatWhatsAppDigits(string digits)
    {
        if (string.IsNullOrEmpty(digits)) return "919052916052";
        if (digits.StartsWith("91")) return digits;
        if (digits.Length == 10) return "91" + digits;
        return digits;
    }

    static string FormatPhoneDisplay(string digits)
    {
        if (string.IsNullOrEmpty(digits)) return "+91 90529 16052";
        if (digits.Length == 10)
            return $"+91 {digits[..5]} {digits[5..]}";
        if (digits.StartsWith("91") && digits.Length >= 12)
        {
            var local = digits[2..];
            if (local.Length == 10)
                return $"+91 {local[..5]} {local[5..]}";
        }
        return "+" + digits;
    }
}
