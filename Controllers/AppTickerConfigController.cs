using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/app")]
[AllowAnonymous]
public class AppTickerConfigController : ControllerBase
{
    readonly ITickerConfigService _tickerConfig;

    public AppTickerConfigController(ITickerConfigService tickerConfig) => _tickerConfig = tickerConfig;

    [HttpGet("ticker-config")]
    public async Task<IActionResult> GetTickerConfig()
    {
        var config = await _tickerConfig.GetPublicConfigAsync();
        return config == null
            ? NotFound(new { success = false, message = "Ticker config not configured." })
            : Ok(config);
    }
}
