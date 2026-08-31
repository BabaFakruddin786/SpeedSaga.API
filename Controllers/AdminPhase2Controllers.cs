using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/admin/themes")]
public class AdminThemesController : ControllerBase
{
    readonly IAdminThemeService _themes;
    readonly AdminOptions _admin;

    public AdminThemesController(IAdminThemeService themes, IOptions<AdminOptions> admin)
    {
        _themes = themes;
        _admin = admin.Value;
    }

    IActionResult? RequireSuperAdmin()
    {
        if (!AdminAuthorization.HasSuperAdminAccess(HttpContext, _admin))
            return AdminAuthorization.HasAdminAccess(HttpContext, _admin)
                ? Forbid()
                : Unauthorized(new ApiResponse<object>(false, "Sign in required"));
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        return Ok(await _themes.ListThemesAsync(ct));
    }

    [HttpPut("{themeCode}")]
    public async Task<IActionResult> Update(string themeCode, [FromBody] UpdateThemeRequest req, CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        var body = req with { ThemeCode = themeCode };
        var result = await _themes.UpdateThemeAsync(body, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{themeCode}/activate")]
    public async Task<IActionResult> Activate(string themeCode, CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        var result = await _themes.SetActiveThemeAsync(themeCode, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

[ApiController]
[Route("api/admin/payment")]
public class AdminPaymentController : ControllerBase
{
    readonly IPaymentConfigService _payment;
    readonly AdminOptions _admin;

    public AdminPaymentController(IPaymentConfigService payment, IOptions<AdminOptions> admin)
    {
        _payment = payment;
        _admin = admin.Value;
    }

    IActionResult? RequireSuperAdmin()
    {
        if (!AdminAuthorization.HasSuperAdminAccess(HttpContext, _admin))
            return AdminAuthorization.HasAdminAccess(HttpContext, _admin)
                ? Forbid()
                : Unauthorized(new ApiResponse<object>(false, "Sign in required"));
        return null;
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        return Ok(await _payment.GetAdminConfigAsync(ct));
    }

    [HttpPut("config")]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdatePaymentConfigRequest req, CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        var result = await _payment.UpdateConfigAsync(req, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

[ApiController]
[Route("api/admin/notifications")]
public class AdminNotificationsController : ControllerBase
{
    readonly IAdminNotificationService _notifications;
    readonly AdminOptions _admin;

    public AdminNotificationsController(IAdminNotificationService notifications, IOptions<AdminOptions> admin)
    {
        _notifications = notifications;
        _admin = admin.Value;
    }

    IActionResult? RequireSuperAdmin()
    {
        if (!AdminAuthorization.HasSuperAdminAccess(HttpContext, _admin))
            return AdminAuthorization.HasAdminAccess(HttpContext, _admin)
                ? Forbid()
                : Unauthorized(new ApiResponse<object>(false, "Sign in required"));
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        return Ok(await _notifications.ListAsync(page, ct));
    }

    [HttpPost("broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] AdminNotificationRequest req, CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        var result = await _notifications.BroadcastAsync(req.Title, req.Body, req.NotifType, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("player/{playerId:guid}")]
    public async Task<IActionResult> SendToPlayer(Guid playerId, [FromBody] AdminNotificationRequest req, CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        var result = await _notifications.SendToPlayerAsync(playerId, req.Title, req.Body, req.NotifType, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

public record AdminNotificationRequest(string Title, string Body, string? NotifType);
