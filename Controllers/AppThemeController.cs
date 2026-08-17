using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/app")]
[AllowAnonymous]
public class AppThemeController : ControllerBase
{
    readonly IThemeService _themes;

    public AppThemeController(IThemeService themes) => _themes = themes;

    [HttpGet("theme")]
    public async Task<IActionResult> GetTheme([FromQuery] string mode = "Dark")
    {
        var theme = await _themes.GetThemeByModeAsync(mode);
        return theme == null
            ? NotFound(new { success = false, message = "Theme not found for mode: " + mode })
            : Ok(theme);
    }

    [HttpGet("theme/default")]
    public async Task<IActionResult> GetDefaultTheme()
    {
        var mode = await _themes.GetDefaultAppearanceModeAsync();
        var theme = await _themes.GetThemeByModeAsync(mode);
        return theme == null
            ? NotFound(new { success = false, message = "Default theme not configured." })
            : Ok(theme);
    }

    [HttpGet("themes")]
    public async Task<IActionResult> ListThemes() => Ok(await _themes.GetAllThemesAsync());
}
