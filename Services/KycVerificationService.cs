using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IKycVerificationService
{
    Task<ApiResponse<object>> SubmitAadhaarAsync(Guid playerId, string aadhaar, string nameOnAadhaar, IFormFile photo);
    Task<ApiResponse<object>> SubmitPanAsync(Guid playerId, string pan, IFormFile photo);
    Task<ApiResponse<object>> SubmitBankAsync(Guid playerId, string account, string ifsc, string holderName, IFormFile? photo);
}

public class KycVerificationService : IKycVerificationService
{
    const long MaxPhotoBytes = 5_000_000;

    readonly ISqlConnectionFactory _db;
    readonly KycDocumentStorage _storage;
    readonly IWebHostEnvironment _env;

    public KycVerificationService(ISqlConnectionFactory db, KycDocumentStorage storage, IWebHostEnvironment env)
    {
        _db = db;
        _storage = storage;
        _env = env;
    }

    public async Task<ApiResponse<object>> SubmitAadhaarAsync(Guid playerId, string aadhaar, string nameOnAadhaar, IFormFile photo)
    {
        aadhaar = aadhaar?.Trim() ?? "";
        nameOnAadhaar = nameOnAadhaar?.Trim() ?? "";
        if (!Regex.IsMatch(aadhaar, @"^\d{12}$"))
            return new ApiResponse<object>(false, "Aadhaar must be exactly 12 digits");
        if (string.IsNullOrWhiteSpace(nameOnAadhaar))
            return new ApiResponse<object>(false, "Enter the name printed on your Aadhaar card");
        if (photo == null || photo.Length == 0)
            return new ApiResponse<object>(false, "Upload a photo of your Aadhaar card");
        if (photo.Length > MaxPhotoBytes)
            return new ApiResponse<object>(false, "Photo must be 5 MB or smaller");

        var profileName = await GetUsernameAsync(playerId);
        if (string.IsNullOrWhiteSpace(profileName))
            return new ApiResponse<object>(false, "Save your full name (as on Aadhaar) in Personal Data first");
        if (!KycNameMatcher.NamesMatch(nameOnAadhaar, profileName))
            return new ApiResponse<object>(false, "Name on Aadhaar must match your profile full name");

        string docPath;
        try
        {
            await using var stream = photo.OpenReadStream();
            docPath = await _storage.SaveAsync(playerId, "aadhaar", stream, photo.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return new ApiResponse<object>(false, ex.Message);
        }

        var masked = $"XXXX-XXXX-{aadhaar[^4..]}";
        await SetKycDocumentAsync(playerId, "Aadhaar", masked, null, null, "PendingReview", nameOnAadhaar, docPath);
        return new ApiResponse<object>(true, "Aadhaar submitted for review. This is not government e-KYC.");
    }

    public async Task<ApiResponse<object>> SubmitPanAsync(Guid playerId, string pan, IFormFile photo)
    {
        pan = pan?.Trim().ToUpperInvariant() ?? "";
        if (!Regex.IsMatch(pan, @"^[A-Z]{5}\d{4}[A-Z]$"))
            return new ApiResponse<object>(false, "PAN format must be ABCDE1234F");
        if (photo == null || photo.Length == 0)
            return new ApiResponse<object>(false, "Upload a photo of your PAN card");
        if (photo.Length > MaxPhotoBytes)
            return new ApiResponse<object>(false, "Photo must be 5 MB or smaller");

        var profileName = await GetUsernameAsync(playerId);
        if (string.IsNullOrWhiteSpace(profileName))
            return new ApiResponse<object>(false, "Save your full name in Personal Data before submitting PAN");

        var aadhaarName = await GetAadhaarNameOnCardAsync(playerId);
        var nameForPan = !string.IsNullOrWhiteSpace(aadhaarName) ? aadhaarName : profileName;
        if (!KycNameMatcher.PanMatchesHolderInitial(pan, nameForPan) && !_env.IsDevelopment())
            return new ApiResponse<object>(false, "PAN holder initial does not match your profile / Aadhaar name");

        string docPath;
        try
        {
            await using var stream = photo.OpenReadStream();
            docPath = await _storage.SaveAsync(playerId, "pan", stream, photo.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return new ApiResponse<object>(false, ex.Message);
        }

        var masked = $"{pan[..4]}****{pan[^1]}";
        await SetKycDocumentAsync(playerId, "PAN", masked, null, null, "PendingReview", null, docPath);
        return new ApiResponse<object>(true, "PAN submitted for review");
    }

    public async Task<ApiResponse<object>> SubmitBankAsync(Guid playerId, string account, string ifsc, string holderName, IFormFile? photo)
    {
        account = account?.Trim() ?? "";
        ifsc = ifsc?.Trim().ToUpperInvariant() ?? "";
        holderName = holderName?.Trim() ?? "";

        if (!Regex.IsMatch(account, @"^\d{9,18}$"))
            return new ApiResponse<object>(false, "Bank account must be 9 to 18 digits");
        if (!Regex.IsMatch(ifsc, @"^[A-Z]{4}0[A-Z0-9]{6}$"))
            return new ApiResponse<object>(false, "IFSC format must be e.g. SBIN0001234");
        if (string.IsNullOrWhiteSpace(holderName))
            return new ApiResponse<object>(false, "Enter the account holder name");
        if (photo == null || photo.Length == 0)
            return new ApiResponse<object>(false, "Upload a photo of your passbook or cancelled cheque");
        if (photo.Length > MaxPhotoBytes)
            return new ApiResponse<object>(false, "Photo must be 5 MB or smaller");

        var profileName = await GetUsernameAsync(playerId);
        if (string.IsNullOrWhiteSpace(profileName))
            return new ApiResponse<object>(false, "Save your full name in Personal Data first");
        if (!KycNameMatcher.NamesMatch(holderName, profileName))
            return new ApiResponse<object>(false, "Account holder name must match your profile name");

        var aadhaarName = await GetAadhaarNameOnCardAsync(playerId);
        if (!string.IsNullOrWhiteSpace(aadhaarName) && !KycNameMatcher.NamesMatch(holderName, aadhaarName))
            return new ApiResponse<object>(false, "Account holder name must match the name on your Aadhaar");

        string docPath;
        try
        {
            await using var stream = photo.OpenReadStream();
            docPath = await _storage.SaveAsync(playerId, "bank", stream, photo.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return new ApiResponse<object>(false, ex.Message);
        }

        var masked = $"XXXX{account[^4..]}";
        await SetKycDocumentAsync(playerId, "Bank", masked, holderName, ifsc, "PendingReview", null, docPath);
        return new ApiResponse<object>(true, "Bank details submitted for review");
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

    async Task<string?> GetAadhaarNameOnCardAsync(Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT AadhaarNameOnCard FROM PlayerKYC WHERE PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value || result == null ? null : result.ToString();
    }

    async Task SetKycDocumentAsync(Guid playerId, string docType, string docNumber, string? holderName,
        string? ifsc, string status, string? nameOnAadhaar, string? docPath)
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
        cmd.Parameters.AddWithValue("@NameOnAadhaar", (object?)nameOnAadhaar ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DocPath", (object?)docPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RejectReason", DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
