using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SpeedSaga.API.Authorization;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IAdminAuthService
{
    Task<ApiResponse<object>> LoginAsync(AdminLoginRequest req, CancellationToken ct = default);
    Task<object?> GetProfileAsync(ClaimsPrincipal user, CancellationToken ct = default);
}

public class AdminAuthService : IAdminAuthService
{
    readonly ISqlConnectionFactory _db;
    readonly JwtSettings _jwt;

    public AdminAuthService(ISqlConnectionFactory db, IOptions<JwtSettings> jwt)
    {
        _db = db;
        _jwt = jwt.Value;
    }

    public async Task<ApiResponse<object>> LoginAsync(AdminLoginRequest req, CancellationToken ct = default)
    {
        var contact = req.Contact?.Trim() ?? "";
        var password = req.Password ?? "";
        if (string.IsNullOrWhiteSpace(contact))
            return new ApiResponse<object>(false, "Enter email or mobile number");
        if (string.IsNullOrWhiteSpace(password))
            return new ApiResponse<object>(false, "Enter password");

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminLogin", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Contact", contact);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct))
            return new ApiResponse<object>(false, "Invalid email/mobile or password");

        var adminUserId = rdr.GetGuid(rdr.GetOrdinal("AdminUserId"));
        var hash = rdr["PasswordHash"].ToString() ?? "";
        var salt = rdr["PasswordSalt"].ToString() ?? "";
        var role = rdr["Role"].ToString() ?? "";
        var displayName = rdr["DisplayName"]?.ToString() ?? "Admin";
        var email = rdr["Email"]?.ToString();
        var phone = rdr["Phone"]?.ToString();
        var isActive = (bool)rdr["IsActive"];
        await rdr.CloseAsync();

        if (!isActive)
            return new ApiResponse<object>(false, "This account is disabled");
        if (!PasswordHasher.Verify(password, salt, hash))
            return new ApiResponse<object>(false, "Invalid email/mobile or password");
        if (role is not (AppRoles.SuperAdmin or AppRoles.Support))
            return new ApiResponse<object>(false, "Invalid admin role");

        await TouchLoginAsync(cn, adminUserId, ct);
        var token = GenerateToken(adminUserId, email ?? phone ?? contact, role, displayName);

        return new ApiResponse<object>(true, "Login successful", new
        {
            token,
            role,
            displayName,
            email,
            phone
        });
    }

    public async Task<object?> GetProfileAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var adminUserIdClaim = user.FindFirst(AppClaimTypes.AdminUserId)?.Value;
        if (string.IsNullOrWhiteSpace(adminUserIdClaim) || !Guid.TryParse(adminUserIdClaim, out var adminUserId))
            return null;

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
            SELECT AdminUserId, Email, Phone, DisplayName, Role, LastLoginAt
            FROM AdminUsers WHERE AdminUserId = @AdminUserId AND IsActive = 1", cn);
        cmd.Parameters.AddWithValue("@AdminUserId", adminUserId);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return null;

        return new
        {
            adminUserId = rdr["AdminUserId"].ToString(),
            email = rdr["Email"]?.ToString(),
            phone = rdr["Phone"]?.ToString(),
            displayName = rdr["DisplayName"]?.ToString(),
            role = rdr["Role"]?.ToString(),
            lastLoginAt = rdr["LastLoginAt"] == DBNull.Value ? (DateTime?)null : (DateTime)rdr["LastLoginAt"]
        };
    }

    static async Task TouchLoginAsync(SqlConnection cn, Guid adminUserId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("USP_AdminTouchLogin", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@AdminUserId", adminUserId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    string GenerateToken(Guid adminUserId, string contact, string role, string displayName)
    {
        var claims = new List<Claim>
        {
            new(AppClaimTypes.AdminUserId, adminUserId.ToString()),
            new(AppClaimTypes.Contact, contact),
            new(ClaimTypes.Role, role),
            new(ClaimTypes.Name, displayName)
        };

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
}
