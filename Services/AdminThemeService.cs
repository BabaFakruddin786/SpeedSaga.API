using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IAdminThemeService
{
    Task<IReadOnlyList<object>> ListThemesAsync(CancellationToken ct = default);
    Task<ApiResponse<object>> UpdateThemeAsync(UpdateThemeRequest req, CancellationToken ct = default);
    Task<ApiResponse<object>> SetActiveThemeAsync(string themeCode, CancellationToken ct = default);
}

public record UpdateThemeRequest(
    string ThemeCode,
    string ThemeName,
    int SortOrder,
    string Bg,
    string Surface,
    string Card,
    string Border,
    string Gold,
    string GoldDim,
    string Accent,
    string AccentBright,
    string Green,
    string Red,
    string Orange,
    string TextColor,
    string TextMuted,
    string TextDim,
    string WalletTop,
    string WalletBottom,
    string FreeModeTop,
    string FreeModeBottom,
    string PremiumTop,
    string PremiumBottom);

public class AdminThemeService : IAdminThemeService
{
    readonly ISqlConnectionFactory _db;

    public AdminThemeService(ISqlConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<object>> ListThemesAsync(CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminListThemes", cn) { CommandType = CommandType.StoredProcedure };
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            list.Add(new
            {
                themeId = (int)rdr["ThemeId"],
                themeCode = rdr["ThemeCode"].ToString(),
                themeName = rdr["ThemeName"].ToString(),
                appearanceMode = rdr["AppearanceMode"].ToString(),
                isActive = (bool)rdr["IsActive"],
                sortOrder = (int)rdr["SortOrder"],
                bg = rdr["Bg"].ToString(),
                surface = rdr["Surface"].ToString(),
                card = rdr["Card"].ToString(),
                border = rdr["Border"].ToString(),
                gold = rdr["Gold"].ToString(),
                goldDim = rdr["GoldDim"].ToString(),
                accent = rdr["Accent"].ToString(),
                accentBright = rdr["AccentBright"].ToString(),
                green = rdr["Green"].ToString(),
                red = rdr["Red"].ToString(),
                orange = rdr["Orange"].ToString(),
                textColor = rdr["TextColor"].ToString(),
                textMuted = rdr["TextMuted"].ToString(),
                textDim = rdr["TextDim"].ToString(),
                walletTop = rdr["WalletTop"].ToString(),
                walletBottom = rdr["WalletBottom"].ToString(),
                freeModeTop = rdr["FreeModeTop"].ToString(),
                freeModeBottom = rdr["FreeModeBottom"].ToString(),
                premiumTop = rdr["PremiumTop"].ToString(),
                premiumBottom = rdr["PremiumBottom"].ToString(),
                updatedAt = (DateTime)rdr["UpdatedAt"]
            });
        }
        return list;
    }

    public async Task<ApiResponse<object>> UpdateThemeAsync(UpdateThemeRequest req, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminUpdateTheme", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@ThemeCode", req.ThemeCode);
        cmd.Parameters.AddWithValue("@ThemeName", req.ThemeName);
        cmd.Parameters.AddWithValue("@SortOrder", req.SortOrder);
        cmd.Parameters.AddWithValue("@Bg", req.Bg);
        cmd.Parameters.AddWithValue("@Surface", req.Surface);
        cmd.Parameters.AddWithValue("@Card", req.Card);
        cmd.Parameters.AddWithValue("@Border", req.Border);
        cmd.Parameters.AddWithValue("@Gold", req.Gold);
        cmd.Parameters.AddWithValue("@GoldDim", req.GoldDim);
        cmd.Parameters.AddWithValue("@Accent", req.Accent);
        cmd.Parameters.AddWithValue("@AccentBright", req.AccentBright);
        cmd.Parameters.AddWithValue("@Green", req.Green);
        cmd.Parameters.AddWithValue("@Red", req.Red);
        cmd.Parameters.AddWithValue("@Orange", req.Orange);
        cmd.Parameters.AddWithValue("@TextColor", req.TextColor);
        cmd.Parameters.AddWithValue("@TextMuted", req.TextMuted);
        cmd.Parameters.AddWithValue("@TextDim", req.TextDim);
        cmd.Parameters.AddWithValue("@WalletTop", req.WalletTop);
        cmd.Parameters.AddWithValue("@WalletBottom", req.WalletBottom);
        cmd.Parameters.AddWithValue("@FreeModeTop", req.FreeModeTop);
        cmd.Parameters.AddWithValue("@FreeModeBottom", req.FreeModeBottom);
        cmd.Parameters.AddWithValue("@PremiumTop", req.PremiumTop);
        cmd.Parameters.AddWithValue("@PremiumBottom", req.PremiumBottom);
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        messageParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);
        var code = (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        return code == 0
            ? new ApiResponse<object>(true, message)
            : new ApiResponse<object>(false, message);
    }

    public async Task<ApiResponse<object>> SetActiveThemeAsync(string themeCode, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminSetActiveTheme", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@ThemeCode", themeCode);
        var resultParam = cmd.Parameters.Add("@Result", SqlDbType.Int);
        resultParam.Direction = ParameterDirection.Output;
        var messageParam = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        messageParam.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);
        var code = (int)resultParam.Value;
        var message = messageParam.Value?.ToString() ?? "";
        return code == 0
            ? new ApiResponse<object>(true, message)
            : new ApiResponse<object>(false, message);
    }
}
