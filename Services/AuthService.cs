using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SpeedSaga.API.Authorization;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IAuthService
{
    Task<ApiResponse<object>> RegisterAsync(RegisterRequest req);
    Task<ApiResponse<object>> LoginAsync(LoginRequest req);
    Task<ApiResponse<object>> ForgotPasswordAsync(ForgotPasswordRequest req, bool includeDevCode = false);
    Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordRequest req);
}

public class AuthService : IAuthService
{
    readonly ISqlConnectionFactory _db;
    readonly JwtSettings _jwt;
    readonly IOtpService _otp;

    public AuthService(ISqlConnectionFactory db, IOptions<JwtSettings> jwt, IOtpService otp)
    {
        _db = db;
        _jwt = jwt.Value;
        _otp = otp;
    }

    public async Task<ApiResponse<object>> RegisterAsync(RegisterRequest req)
    {
        var salt = GenerateSalt();
        var hash = HashPassword(req.Password, salt);

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_RegisterPlayer", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@ContactEmail", (object?)req.ContactEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ContactPhone", (object?)req.ContactPhone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PasswordHash", hash);
        cmd.Parameters.AddWithValue("@PasswordSalt", salt);
        cmd.Parameters.AddWithValue("@ReferralCode", (object?)req.ReferralCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StateCode", (object?)req.StateCode ?? DBNull.Value);

        var pId = cmd.Parameters.Add("@NewPlayerId", SqlDbType.UniqueIdentifier);
        pId.Direction = ParameterDirection.Output;
        var pRes = cmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;
        var pMsg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();

        var result = (int)pRes.Value!;
        var msg = (string)pMsg.Value!;

        if (result != 1)
            return new ApiResponse<object>(false, msg);

        var playerId = (Guid)pId.Value!;
        var contact = req.ContactEmail ?? req.ContactPhone!;
        var token = GenerateToken(playerId, contact, req.StateCode);

        return new ApiResponse<object>(true, "Registration successful", new { PlayerId = playerId, Token = token });
    }

    public async Task<ApiResponse<object>> LoginAsync(LoginRequest req)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_LoginPlayer", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Contact", req.Contact);

        var pId = cmd.Parameters.Add("@PlayerId", SqlDbType.UniqueIdentifier);
        pId.Direction = ParameterDirection.Output;
        var pHash = cmd.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 512);
        pHash.Direction = ParameterDirection.Output;
        var pSalt = cmd.Parameters.Add("@PasswordSalt", SqlDbType.NVarChar, 256);
        pSalt.Direction = ParameterDirection.Output;
        var pStat = cmd.Parameters.Add("@StateCode", SqlDbType.NVarChar, 10);
        pStat.Direction = ParameterDirection.Output;
        var pRes = cmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync();

        var r = (int)pRes.Value!;
        if (r == 0) return new ApiResponse<object>(false, "Account not found.");
        if (r == -1) return new ApiResponse<object>(false, "Account is banned.");

        var storedHash = (string)pHash.Value!;
        var storedSalt = (string)pSalt.Value!;
        var inputHash = HashPassword(req.Password, storedSalt);

        if (inputHash != storedHash)
            return new ApiResponse<object>(false, "Invalid password.");

        var playerId = (Guid)pId.Value!;
        var stateCode = pStat.Value == DBNull.Value ? null : (string)pStat.Value;
        var token = GenerateToken(playerId, req.Contact, stateCode);

        return new ApiResponse<object>(true, "Login successful", new { PlayerId = playerId, Token = token });
    }

    public async Task<ApiResponse<object>> ForgotPasswordAsync(ForgotPasswordRequest req, bool includeDevCode = false)
    {
        var contact = req.Contact?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(contact))
            return new ApiResponse<object>(true, "If the account exists, a reset code has been sent.");

        var channel = ResolveChannel(req.Channel, contact);
        var playerId = await FindPlayerIdAsync(contact);

        // Always return generic success to avoid account enumeration
        if (playerId == null)
            return new ApiResponse<object>(true, "If the account exists, a reset code has been sent.");

        var otpResult = await _otp.SendAsync(new OtpSendRequest(
            playerId,
            OtpPurposes.PasswordReset,
            channel,
            contact,
            IncludeDevOtpInResponse: includeDevCode));

        if (!otpResult.Success)
            return new ApiResponse<object>(false, otpResult.Message, new { retryAfterSeconds = otpResult.RetryAfterSeconds });

        object? data = includeDevCode && !string.IsNullOrEmpty(otpResult.DevOtp)
            ? new { resetCode = otpResult.DevOtp, expiresInMinutes = 10, refId = otpResult.RefId }
            : null;

        return new ApiResponse<object>(true, "If the account exists, a reset code has been sent.", data);
    }

    public async Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordRequest req)
    {
        var contact = req.Contact?.Trim() ?? "";
        var verify = await _otp.VerifyAsync(new OtpVerifyRequest(
            req.ResetCode,
            req.RefId,
            Destination: contact,
            Purpose: OtpPurposes.PasswordReset));

        if (!verify.Success)
            return new ApiResponse<object>(false, verify.Message);

        var playerId = await FindPlayerIdAsync(contact);
        if (playerId == null)
            return new ApiResponse<object>(false, "Invalid reset request.");

        var salt = GenerateSalt();
        var hash = HashPassword(req.NewPassword, salt);

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE Players SET PasswordHash = @Hash, PasswordSalt = @Salt WHERE PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@Hash", hash);
        cmd.Parameters.AddWithValue("@Salt", salt);
        cmd.Parameters.AddWithValue("@PlayerId", playerId.Value);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
            return new ApiResponse<object>(false, "Invalid reset request.");

        return new ApiResponse<object>(true, "Password reset successful.");
    }

    static string ResolveChannel(string? channel, string contact)
    {
        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (channel.Equals("email", StringComparison.OrdinalIgnoreCase)) return MessageChannels.Email;
            if (channel.Equals("mobile", StringComparison.OrdinalIgnoreCase) ||
                channel.Equals("sms", StringComparison.OrdinalIgnoreCase)) return MessageChannels.Sms;
        }
        return contact.Contains('@') ? MessageChannels.Email : MessageChannels.Sms;
    }

    async Task<Guid?> FindPlayerIdAsync(string contact)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT PlayerId FROM Players WHERE (ContactEmail = @Contact OR ContactPhone = @Contact) AND IsActive = 1", cn);
        cmd.Parameters.AddWithValue("@Contact", contact);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : (Guid)result;
    }

    private string GenerateToken(Guid playerId, string contact, string? stateCode)
    {
        var claims = new List<Claim>
        {
            new(AppClaimTypes.PlayerId, playerId.ToString()),
            new(AppClaimTypes.Contact, contact),
            new(ClaimTypes.Role, AppRoles.Player)
        };

        if (!string.IsNullOrWhiteSpace(stateCode))
            claims.Add(new Claim(AppClaimTypes.StateCode, stateCode));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(_jwt.ExpireHours);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateSalt()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashPassword(string password, string salt)
    {
        using var sha = SHA256.Create();
        var combined = Encoding.UTF8.GetBytes(password + salt);
        return Convert.ToBase64String(sha.ComputeHash(combined));
    }
}
