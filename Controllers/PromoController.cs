using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Extensions;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/promos")]
[Authorize(Policy = Authorization.Policies.PlayerOnly)]
public class PromoController : ControllerBase
{
    readonly IPromoService _promos;

    public PromoController(IPromoService promos) => _promos = promos;

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _promos.GetOffersAsync(User.GetPlayerId()));

    [HttpPost("{code}/claim")]
    public async Task<IActionResult> Claim(string code)
    {
        var result = await _promos.ClaimAsync(User.GetPlayerId(), code);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
