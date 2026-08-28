using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/app")]
[AllowAnonymous]
public class AppSupportConfigController : ControllerBase
{
    readonly ISupportConfigService _supportConfig;

    public AppSupportConfigController(ISupportConfigService supportConfig) => _supportConfig = supportConfig;

    [HttpGet("support-config")]
    public async Task<IActionResult> GetSupportConfig()
    {
        var config = await _supportConfig.GetPublicConfigAsync();
        return config == null
            ? NotFound(new { success = false, message = "Support config not configured." })
            : Ok(config);
    }
}
