using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/admin/auth")]
public class AdminAuthController : ControllerBase
{
    readonly IAdminAuthService _auth;
    readonly AdminOptions _admin;

    public AdminAuthController(IAdminAuthService auth, IOptions<AdminOptions> admin)
    {
        _auth = auth;
        _admin = admin.Value;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest req)
    {
        var result = await _auth.LoginAsync(req);
        return result.Success ? Ok(result) : Unauthorized(result);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!AdminAuthorization.HasAdminAccess(HttpContext, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Sign in required"));
        var profile = await _auth.GetProfileAsync(User);
        return profile == null ? Unauthorized(new ApiResponse<object>(false, "Not signed in")) : Ok(profile);
    }
}
