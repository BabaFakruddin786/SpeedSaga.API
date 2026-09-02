using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IPaymentConfigService
{
    Task<PaymentConfigAdminDto?> GetAdminConfigAsync(CancellationToken ct = default);
    Task<PaymentConfigSecrets> GetSecretsAsync(CancellationToken ct = default);
    Task<ApiResponse<object>> UpdateConfigAsync(UpdatePaymentConfigRequest req, CancellationToken ct = default);
}

public record PaymentConfigSecrets(
    bool IsRazorpayEnabled,
    string KeyId,
    string KeySecret,
    string WebhookSecret,
    bool IsPushEnabled,
    string? FcmServerKey,
    long MinDepositPaise,
    long MinWithdrawPaise,
    long MaxWithdrawPaise);

public record PaymentConfigAdminDto(
    bool IsRazorpayEnabled,
    string RazorpayKeyId,
    bool HasKeySecret,
    string KeySecretMasked,
    bool HasWebhookSecret,
    string WebhookSecretMasked,
    string? CompanyBankName,
    string? CompanyBankAccount,
    string? CompanyBankIfsc,
    string? CompanyBankHolder,
    long MinDepositPaise,
    long MinWithdrawPaise,
    long MaxWithdrawPaise,
    bool IsPushEnabled,
    bool HasFcmServerKey,
    string? FcmServerKeyMasked,
    DateTime UpdatedAt);

public record UpdatePaymentConfigRequest(
    bool IsRazorpayEnabled,
    string? RazorpayKeyId,
    string? RazorpayKeySecret,
    string? RazorpayWebhookSecret,
    string? CompanyBankName,
    string? CompanyBankAccount,
    string? CompanyBankIfsc,
    string? CompanyBankHolder,
    long MinDepositPaise,
    long MinWithdrawPaise,
    long MaxWithdrawPaise,
    bool IsPushEnabled,
    string? FcmServerKey);

public class PaymentConfigService : IPaymentConfigService
{
    const string SecretsCacheKey = "payment_config_secrets";

    readonly ISqlConnectionFactory _db;
    readonly IMemoryCache _cache;
    readonly IConfiguration _configuration;

    public PaymentConfigService(ISqlConnectionFactory db, IMemoryCache cache, IConfiguration configuration)
    {
        _db = db;
        _cache = cache;
        _configuration = configuration;
    }

    public async Task<PaymentConfigAdminDto?> GetAdminConfigAsync(CancellationToken ct = default)
    {
        var row = await LoadRowAsync(ct);
        return row == null ? BuildFallbackAdminDto() : MapAdmin(row, _configuration);
    }

    public async Task<PaymentConfigSecrets> GetSecretsAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(SecretsCacheKey, out PaymentConfigSecrets? cached) && cached != null)
            return cached;

        var row = await LoadRowAsync(ct);
        var secrets = row == null ? BuildFallbackSecrets() : MapSecrets(row, _configuration);
        _cache.Set(SecretsCacheKey, secrets, TimeSpan.FromMinutes(2));
        return secrets;
    }

    public async Task<ApiResponse<object>> UpdateConfigAsync(UpdatePaymentConfigRequest req, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_UpdateAppPaymentConfig", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@IsRazorpayEnabled", req.IsRazorpayEnabled);
        cmd.Parameters.AddWithValue("@RazorpayKeyId", req.RazorpayKeyId ?? "");
        cmd.Parameters.AddWithValue("@RazorpayKeySecret", req.RazorpayKeySecret ?? "");
        cmd.Parameters.AddWithValue("@RazorpayWebhookSecret", req.RazorpayWebhookSecret ?? "");
        cmd.Parameters.AddWithValue("@CompanyBankName", (object?)req.CompanyBankName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompanyBankAccount", (object?)req.CompanyBankAccount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompanyBankIfsc", (object?)req.CompanyBankIfsc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompanyBankHolder", (object?)req.CompanyBankHolder ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MinDepositPaise", req.MinDepositPaise);
        cmd.Parameters.AddWithValue("@MinWithdrawPaise", req.MinWithdrawPaise);
        cmd.Parameters.AddWithValue("@MaxWithdrawPaise", req.MaxWithdrawPaise);
        cmd.Parameters.AddWithValue("@FcmServerKey", (object?)req.FcmServerKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsPushEnabled", req.IsPushEnabled);
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        messageParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);

        var code = (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        if (code != 0)
            return new ApiResponse<object>(false, message);

        _cache.Remove(SecretsCacheKey);
        var updated = await GetAdminConfigAsync(ct);
        return new ApiResponse<object>(true, message, updated);
    }

    async Task<Dictionary<string, object>?> LoadRowAsync(CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_GetAppPaymentConfig", cn) { CommandType = CommandType.StoredProcedure };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return null;
        var row = new Dictionary<string, object>();
        for (var i = 0; i < rdr.FieldCount; i++)
            row[rdr.GetName(i)] = rdr.GetValue(i);
        return row;
    }

    PaymentConfigAdminDto BuildFallbackAdminDto()
    {
        var keyId = _configuration["Razorpay:KeyId"] ?? "";
        var secret = _configuration["Razorpay:KeySecret"] ?? "";
        var webhook = _configuration["Razorpay:WebhookSecret"] ?? "";
        return new PaymentConfigAdminDto(
            true, keyId, !string.IsNullOrEmpty(secret), Mask(secret), !string.IsNullOrEmpty(webhook), Mask(webhook),
            null, null, null, null, 10000, 10000, 50000000, false, false, null, DateTime.UtcNow);
    }

    PaymentConfigSecrets BuildFallbackSecrets() => new(
        true,
        _configuration["Razorpay:KeyId"] ?? "",
        _configuration["Razorpay:KeySecret"] ?? "",
        _configuration["Razorpay:WebhookSecret"] ?? "",
        false,
        null,
        10000,
        10000,
        50000000);

    static PaymentConfigAdminDto MapAdmin(Dictionary<string, object> row, IConfiguration configuration)
    {
        var keyId = row["RazorpayKeyId"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(keyId))
            keyId = configuration["Razorpay:KeyId"] ?? "";

        var keySecret = row["RazorpayKeySecret"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(keySecret))
            keySecret = configuration["Razorpay:KeySecret"] ?? "";

        var webhookSecret = row["RazorpayWebhookSecret"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(webhookSecret))
            webhookSecret = configuration["Razorpay:WebhookSecret"] ?? "";

        var fcm = row["FcmServerKey"]?.ToString();
        return new PaymentConfigAdminDto(
            (bool)row["IsRazorpayEnabled"],
            keyId,
            !string.IsNullOrEmpty(keySecret),
            Mask(keySecret),
            !string.IsNullOrEmpty(webhookSecret),
            Mask(webhookSecret),
            row["CompanyBankName"]?.ToString(),
            row["CompanyBankAccount"]?.ToString(),
            row["CompanyBankIfsc"]?.ToString(),
            row["CompanyBankHolder"]?.ToString(),
            (long)row["MinDepositPaise"],
            (long)row["MinWithdrawPaise"],
            (long)row["MaxWithdrawPaise"],
            (bool)row["IsPushEnabled"],
            !string.IsNullOrEmpty(fcm),
            Mask(fcm),
            row["UpdatedAt"] is DateTime dt ? dt : DateTime.UtcNow);
    }

    static PaymentConfigSecrets MapSecrets(Dictionary<string, object> row, IConfiguration configuration)
    {
        var keyId = row["RazorpayKeyId"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(keyId))
            keyId = configuration["Razorpay:KeyId"] ?? "";

        var keySecret = row["RazorpayKeySecret"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(keySecret))
            keySecret = configuration["Razorpay:KeySecret"] ?? "";

        var webhookSecret = row["RazorpayWebhookSecret"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(webhookSecret))
            webhookSecret = configuration["Razorpay:WebhookSecret"] ?? "";

        return new PaymentConfigSecrets(
            (bool)row["IsRazorpayEnabled"],
            keyId,
            keySecret,
            webhookSecret,
            (bool)row["IsPushEnabled"],
            row["FcmServerKey"]?.ToString(),
            (long)row["MinDepositPaise"],
            (long)row["MinWithdrawPaise"],
            (long)row["MaxWithdrawPaise"]);
    }

    static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= 4) return "****";
        return new string('*', Math.Min(value.Length - 4, 12)) + value[^4..];
    }
}
