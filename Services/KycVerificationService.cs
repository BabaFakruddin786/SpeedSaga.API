using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IKycVerificationService
{
    Task<ApiResponse<object>> SendAadhaarOtpAsync(Guid playerId, string aadhaar);
    Task<ApiResponse<object>> VerifyAadhaarOtpAsync(Guid playerId, string refId, string otp);
    Task<ApiResponse<object>> VerifyPanAsync(Guid playerId, string pan);
    Task<ApiResponse<object>> VerifyBankAsync(Guid playerId, string account, string ifsc, string holderName);
}

public class KycVerificationService : IKycVerificationService
{
    readonly ISqlConnectionFactory _db;
    readonly IOtpService _otp;
    readonly IWebHostEnvironment _env;

    public KycVerificationService(ISqlConnectionFactory db, IOtpService otp, IWebHostEnvironment env)
    {
        _db = db;
        _otp = otp;
        _env = env;
    }

    public async Task<ApiResponse<object>> SendAadhaarOtpAsync(Guid playerId, string aadhaar)
    {
        aadhaar = aadhaar?.Trim() ?? "";
        if (!Regex.IsMatch(aadhaar, @"^\d{12}$"))
            return new ApiResponse<object>(false, "Aadhaar must be exactly 12 digits");

        var phone = await GetPlayerPhoneAsync(playerId);
        if (string.IsNullOrWhiteSpace(phone))
            return new ApiResponse<object>(false, "Add a mobile number to your account before Aadhaar verification");

        var maskedAadhaar = $"XXXX-XXXX-{aadhaar[^4..]}";
        var context = JsonSerializer.Serialize(new { aadhaarMasked = maskedAadhaar, aadhaarLast4 = aadhaar[^4..] });

        var result = await _otp.SendAsync(new OtpSendRequest(
            playerId,
            OtpPurposes.KycAadhaar,
            MessageChannels.Sms,
            phone,
            context,
            IncludeDevOtpInResponse: true));

        if (!result.Success)
            return new ApiResponse<object>(false, result.Message, new { retryAfterSeconds = result.RetryAfterSeconds });

        var data = new Dictionary<string, object?> { ["refId"] = result.RefId };
        if (!string.IsNullOrEmpty(result.DevOtp))
            data["devOtp"] = result.DevOtp;

        return new ApiResponse<object>(true, result.Message, data);
    }

    public async Task<ApiResponse<object>> VerifyAadhaarOtpAsync(Guid playerId, string refId, string otp)
    {
        var result = await _otp.VerifyAsync(new OtpVerifyRequest(otp, refId, playerId, Purpose: OtpPurposes.KycAadhaar));
        if (!result.Success)
            return new ApiResponse<object>(false, result.Message);

        string masked = "XXXX-XXXX-****";
        if (!string.IsNullOrWhiteSpace(result.ContextJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(result.ContextJson);
                if (doc.RootElement.TryGetProperty("aadhaarMasked", out var el))
                    masked = el.GetString() ?? masked;
            }
            catch { /* use default */ }
        }

        await SetKycDocumentAsync(playerId, "Aadhaar", masked, null, null, "Verified");
        return new ApiResponse<object>(true, "Aadhaar verified successfully");
    }

    public async Task<ApiResponse<object>> VerifyPanAsync(Guid playerId, string pan)
    {
        pan = pan?.Trim().ToUpperInvariant() ?? "";
        if (!Regex.IsMatch(pan, @"^[A-Z]{5}\d{4}[A-Z]$"))
            return new ApiResponse<object>(false, "PAN format must be ABCDE1234F");

        var username = await GetUsernameAsync(playerId);
        if (string.IsNullOrWhiteSpace(username))
            return new ApiResponse<object>(false, "Set your display name in Personal Data before verifying PAN");

        var expectedInitial = char.ToUpperInvariant(username.Trim()[0]);
        var panInitial = pan[3];
        if (panInitial != expectedInitial && !_env.IsDevelopment())
            return new ApiResponse<object>(false, "PAN does not match your profile name. Use the PAN registered in your name.");

        var masked = $"{pan[..4]}****{pan[^1]}";
        await SetKycDocumentAsync(playerId, "PAN", masked, username, null, "Verified");
        return new ApiResponse<object>(true, "PAN verified and linked to your profile");
    }

    public async Task<ApiResponse<object>> VerifyBankAsync(Guid playerId, string account, string ifsc, string holderName)
    {
        account = account?.Trim() ?? "";
        ifsc = ifsc?.Trim().ToUpperInvariant() ?? "";
        holderName = holderName?.Trim() ?? "";

        if (!Regex.IsMatch(account, @"^\d{9,18}$"))
            return new ApiResponse<object>(false, "Bank account must be 9 to 18 digits");
        if (!Regex.IsMatch(ifsc, @"^[A-Z]{4}0[A-Z0-9]{6}$"))
            return new ApiResponse<object>(false, "IFSC format must be e.g. SBIN0001234");

        var username = await GetUsernameAsync(playerId);
        if (string.IsNullOrWhiteSpace(username))
            return new ApiResponse<object>(false, "Set your display name in Personal Data before verifying bank account");

        if (!NamesMatch(holderName, username))
            return new ApiResponse<object>(false, "Account holder name must match your profile display name");

        var masked = $"XXXX{account[^4..]}";
        await SetKycDocumentAsync(playerId, "Bank", masked, holderName, ifsc, "Verified");
        return new ApiResponse<object>(true, "Bank account verified (penny-drop simulated in sandbox)");
    }

    static bool NamesMatch(string holder, string profileName)
    {
        holder = holder.Trim();
        profileName = profileName.Trim();
        if (holder.Equals(profileName, StringComparison.OrdinalIgnoreCase)) return true;
        var holderFirst = holder.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? holder;
        var profileFirst = profileName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? profileName;
        return holderFirst.Equals(profileFirst, StringComparison.OrdinalIgnoreCase);
    }

    async Task<string?> GetPlayerPhoneAsync(Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT ContactPhone FROM Players WHERE PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value || result == null ? null : result.ToString();
    }

    async Task<string?> GetUsernameAsync(Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Username FROM Players WHERE PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value || result == null ? null : result.ToString();
    }

    async Task SetKycDocumentAsync(Guid playerId, string docType, string docNumber, string? holderName, string? ifsc, string status)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_SetKycDocument", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@DocType", docType);
        cmd.Parameters.AddWithValue("@DocNumber", docNumber);
        cmd.Parameters.AddWithValue("@HolderName", (object?)holderName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Ifsc", (object?)ifsc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", status);
        await cmd.ExecuteNonQueryAsync();
    }
}
