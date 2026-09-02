using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/admin/levels")]
public class AdminLevelsController : ControllerBase
{
    readonly IAdminLevelService _levels;
    readonly AdminOptions _admin;

    public AdminLevelsController(IAdminLevelService levels, IOptions<AdminOptions> admin)
    {
        _levels = levels;
        _admin = admin.Value;
    }

    IActionResult? RequireSuperAdmin()
    {
        if (!AdminAuthorization.HasSuperAdminAccess(HttpContext, _admin))
            return AdminAuthorization.HasAdminAccess(HttpContext, _admin)
                ? StatusCode(403, new ApiResponse<object>(false, "Super Admin access required."))
                : Unauthorized(new ApiResponse<object>(false, "Sign in required"));
        return null;
    }

    IActionResult? RequireAdmin()
    {
        if (!AdminAuthorization.HasAdminAccess(HttpContext, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Sign in required"));
        return null;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        return Ok(await _levels.GetSummaryAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? timeMode,
        [FromQuery] string? puzzleTier,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (RequireAdmin() is { } denied) return denied;
        return Ok(await _levels.ListAsync(timeMode, puzzleTier, isActive, page, pageSize, ct));
    }

    [HttpPost("expand")]
    public async Task<IActionResult> Expand([FromBody] ExpandLevelPoolRequest req, CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        var result = await _levels.ExpandPoolAsync(req.TargetTotal, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("purge-inactive")]
    public async Task<IActionResult> PurgeInactive([FromBody] PurgeInactiveLevelsRequest? req, CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        var result = await _levels.PurgeInactiveAsync(req?.LegacyOnly ?? true, ct);
        return Ok(result);
    }

    [HttpPost("{levelId:int}/active")]
    public async Task<IActionResult> SetActive(int levelId, [FromBody] SetLevelActiveRequest req, CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        var result = await _levels.SetActiveAsync(levelId, req.IsActive, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

public record ExpandLevelPoolRequest(int TargetTotal);
public record PurgeInactiveLevelsRequest(bool LegacyOnly = true);
public record SetLevelActiveRequest(bool IsActive);
