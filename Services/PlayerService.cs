using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IPlayerService
{
    Task<object?> GetDashboardAsync(Guid playerId);
    Task<ApiResponse<object>> UpdateProfileAsync(Guid playerId, UpdateProfileRequest req);
    Task<ApiResponse<object>> SetAppearanceAsync(Guid playerId, string appearanceMode);
    Task<object?> GetKycAsync(Guid playerId);
    Task<ApiResponse<object>> SubmitKycAsync(Guid playerId, KycSubmitRequest req);
    Task<ApiResponse<object>> DevApproveKycAsync(Guid playerId);
}

public class PlayerService : IPlayerService
{
    private readonly ISqlConnectionFactory _db;
    private readonly IWebHostEnvironment _env;

    public PlayerService(ISqlConnectionFactory db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<object?> GetDashboardAsync(Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetPlayerDashboard", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;

        return new
        {
            PlayerId = rdr["PlayerId"].ToString(),
            ContactEmail = rdr["ContactEmail"]?.ToString(),
            ContactPhone = rdr["ContactPhone"]?.ToString(),
            Username = rdr["Username"]?.ToString(),
            StateCode = rdr["StateCode"]?.ToString(),
            BalanceRs = (long)rdr["BalancePaise"] / 100.0,
            BalancePaise = (long)rdr["BalancePaise"],
            DepositPaise = (long)rdr["DepositPaise"],
            WinningPaise = (long)rdr["WinningPaise"],
            WithdrawnPaise = (long)rdr["WithdrawnPaise"],
            TotalGames = (int)rdr["TotalGames"],
            TotalWins = (int)rdr["TotalWins"],
            TotalLosses = (int)rdr["TotalLosses"],
            WinRatePct = (decimal)rdr["WinRatePct"],
            CurrentStreak = (int)rdr["CurrentStreak"],
            BestStreak = (int)rdr["BestStreak"],
            TotalEntryPaise = (long)rdr["TotalEntryPaise"],
            TotalRewardPaise = (long)rdr["TotalRewardPaise"],
            AadhaarStatus = rdr["AadhaarStatus"].ToString(),
            PANStatus = rdr["PANStatus"].ToString(),
            BankStatus = rdr["BankStatus"].ToString(),
            IsFullyVerified = (bool)rdr["IsFullyVerified"],
            ReferralCode = rdr["ReferralCode"]?.ToString(),
            AppearanceMode = rdr["AppearanceMode"]?.ToString() ?? "Dark"
        };
    }

    public async Task<ApiResponse<object>> SetAppearanceAsync(Guid playerId, string appearanceMode)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_SetPlayerAppearance", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@AppearanceMode", appearanceMode);
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        messageParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        var code = (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        return code == 0
            ? new ApiResponse<object>(true, message)
            : new ApiResponse<object>(false, message);
    }

    public async Task<ApiResponse<object>> UpdateProfileAsync(Guid playerId, UpdateProfileRequest req)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_UpdatePlayerProfile", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@Username", (object?)req.Username?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StateCode", (object?)req.StateCode?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ContactEmail", (object?)req.ContactEmail?.Trim().ToLowerInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ContactPhone", (object?)req.ContactPhone?.Trim() ?? DBNull.Value);
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        messageParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        var code = (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        return code == 1
            ? new ApiResponse<object>(true, message)
            : new ApiResponse<object>(false, message);
    }

    public async Task<object?> GetKycAsync(Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetKycStatus", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return new { AadhaarStatus = "NotSubmitted", PANStatus = "NotSubmitted", BankStatus = "NotSubmitted", IsFullyVerified = false };
        return new
        {
            AadhaarStatus = rdr["AadhaarStatus"].ToString(),
            PANStatus = rdr["PANStatus"].ToString(),
            BankStatus = rdr["BankStatus"].ToString(),
            IsFullyVerified = (bool)rdr["IsFullyVerified"],
            AadhaarMasked = rdr["AadhaarMasked"]?.ToString(),
            PANMasked = rdr["PANMasked"]?.ToString(),
            BankMasked = rdr["BankMasked"]?.ToString()
        };
    }

    public async Task<ApiResponse<object>> SubmitKycAsync(Guid playerId, KycSubmitRequest req)
    {
        var doc = req.DocNumber?.Trim() ?? "";
        var err = ValidateKycDocument(req.DocType, doc);
        if (err != null) return new ApiResponse<object>(false, err);

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_SubmitKyc", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@DocType", req.DocType);
        cmd.Parameters.AddWithValue("@DocNumber", doc);
        cmd.Parameters.AddWithValue("@HolderName", (object?)req.HolderName ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return new ApiResponse<object>(true, $"{req.DocType} submitted for verification");
    }

    static string? ValidateKycDocument(string? docType, string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return "Enter document number";
        return docType switch
        {
            "Aadhaar" when !System.Text.RegularExpressions.Regex.IsMatch(number, @"^\d{12}$")
                => "Aadhaar must be exactly 12 digits",
            "PAN" when !System.Text.RegularExpressions.Regex.IsMatch(number.ToUpperInvariant(), @"^[A-Z]{5}\d{4}[A-Z]$")
                => "PAN format must be ABCDE1234F",
            "Bank" when !System.Text.RegularExpressions.Regex.IsMatch(number, @"^\d{9,18}$")
                => "Bank account must be 9 to 18 digits",
            _ => null
        };
    }

    public async Task<ApiResponse<object>> DevApproveKycAsync(Guid playerId)
    {
        if (!_env.IsDevelopment())
            return new ApiResponse<object>(false, "KYC dev approval only available in Development.");

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            UPDATE K SET
                AadhaarStatus = CASE WHEN K.AadhaarStatus IN ('NotSubmitted','Pending') THEN 'Verified' ELSE K.AadhaarStatus END,
                PANStatus = CASE WHEN K.PANStatus IN ('NotSubmitted','Pending') THEN 'Verified' ELSE K.PANStatus END,
                BankStatus = CASE WHEN K.BankStatus IN ('NotSubmitted','Pending') THEN 'Verified' ELSE K.BankStatus END,
                IsFullyVerified = 1,
                UpdatedAt = GETDATE()
            FROM PlayerKYC K
            WHERE K.PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0
            ? new ApiResponse<object>(true, "KYC approved for development testing")
            : new ApiResponse<object>(false, "Player not found");
    }
}
