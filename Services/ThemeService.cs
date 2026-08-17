using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IThemeService
{
    Task<object?> GetThemeByModeAsync(string appearanceMode);
    Task<string> GetDefaultAppearanceModeAsync();
    Task<object?> GetAllThemesAsync();
    Task<ApiResponse<object>> SetPlayerAppearanceAsync(Guid playerId, string appearanceMode);
}

public class ThemeService : IThemeService
{
    readonly ISqlConnectionFactory _db;

    public ThemeService(ISqlConnectionFactory db) => _db = db;

    public async Task<object?> GetThemeByModeAsync(string appearanceMode)
    {
        var mode = NormalizeMode(appearanceMode);
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetAppTheme", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@AppearanceMode", mode);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;
        return MapTheme(rdr);
    }

    public async Task<string> GetDefaultAppearanceModeAsync()
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetAppThemeDefault", cn) { CommandType = CommandType.StoredProcedure };
        var result = await cmd.ExecuteScalarAsync();
        var mode = result?.ToString();
        return NormalizeMode(mode);
    }

    public async Task<object?> GetAllThemesAsync()
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetAllAppThemes", cn) { CommandType = CommandType.StoredProcedure };
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new
            {
                ThemeCode = rdr["ThemeCode"].ToString(),
                ThemeName = rdr["ThemeName"].ToString(),
                AppearanceMode = rdr["AppearanceMode"].ToString(),
                IsActive = (bool)rdr["IsActive"],
                SortOrder = (int)rdr["SortOrder"],
                UpdatedAt = (DateTime)rdr["UpdatedAt"]
            });
        }
        return list;
    }

    public async Task<ApiResponse<object>> SetPlayerAppearanceAsync(Guid playerId, string appearanceMode)
    {
        var mode = NormalizeMode(appearanceMode);
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_SetPlayerAppearance", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@AppearanceMode", mode);
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

    static string NormalizeMode(string? mode)
        => string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";

    static object MapTheme(SqlDataReader rdr) => new
    {
        ThemeCode = rdr["ThemeCode"].ToString(),
        ThemeName = rdr["ThemeName"].ToString(),
        AppearanceMode = rdr["AppearanceMode"].ToString(),
        IsActive = (bool)rdr["IsActive"],
        Bg = rdr["Bg"].ToString(),
        Surface = rdr["Surface"].ToString(),
        Card = rdr["Card"].ToString(),
        Border = rdr["Border"].ToString(),
        Gold = rdr["Gold"].ToString(),
        GoldDim = rdr["GoldDim"].ToString(),
        Accent = rdr["Accent"].ToString(),
        AccentBright = rdr["AccentBright"].ToString(),
        Green = rdr["Green"].ToString(),
        Red = rdr["Red"].ToString(),
        Orange = rdr["Orange"].ToString(),
        Text = rdr["TextColor"].ToString(),
        TextMuted = rdr["TextMuted"].ToString(),
        TextDim = rdr["TextDim"].ToString(),
        WalletTop = rdr["WalletTop"].ToString(),
        WalletBottom = rdr["WalletBottom"].ToString(),
        FreeModeTop = rdr["FreeModeTop"].ToString(),
        FreeModeBottom = rdr["FreeModeBottom"].ToString(),
        PremiumTop = rdr["PremiumTop"].ToString(),
        PremiumBottom = rdr["PremiumBottom"].ToString(),
        UpdatedAt = (DateTime)rdr["UpdatedAt"]
    };
}
