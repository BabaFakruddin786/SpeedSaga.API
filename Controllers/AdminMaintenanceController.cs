using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/admin/maintenance")]
public class AdminMaintenanceController : ControllerBase
{
    public const string PurgeConfirmationPhrase = "DELETE ALL TEST DATA";

    readonly IAdminTestDataService _testData;
    readonly AdminOptions _admin;

    public AdminMaintenanceController(IAdminTestDataService testData, IOptions<AdminOptions> admin)
    {
        _testData = testData;
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

    [HttpGet("test-data")]
    public async Task<IActionResult> GetTestDataStatus(CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;

        var counts = _testData.IsEnabled ? await _testData.GetCountsAsync(ct) : null;
        return Ok(new
        {
            enabled = _testData.IsEnabled,
            confirmationPhrase = PurgeConfirmationPhrase,
            counts
        });
    }

    [HttpPost("purge-all-test-data")]
    public async Task<IActionResult> PurgeAll([FromBody] PurgeAllTestDataRequest req, CancellationToken ct)
    {
        if (RequireSuperAdmin() is { } denied) return denied;
        if (!_testData.IsEnabled)
            return BadRequest(new ApiResponse<object>(false, "Test data purge is disabled on this server."));

        if (!string.Equals(req?.Confirmation?.Trim(), PurgeConfirmationPhrase, StringComparison.Ordinal))
            return BadRequest(new ApiResponse<object>(false, $"Type exactly: {PurgeConfirmationPhrase}"));

        var result = await _testData.PurgeAllAsync(ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

public record PurgeAllTestDataRequest(string? Confirmation);
