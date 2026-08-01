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
}

public class AuthService : IAuthService
{
    private readonly ISqlConnectionFactory _db;
    private readonly JwtSettings _jwt;

    public AuthService(ISqlConnectionFactory db, IOptions<JwtSettings> jwt)
    {
        _db = db;
        _jwt = jwt.Value;
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
