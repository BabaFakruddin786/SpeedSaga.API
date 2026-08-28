using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/admin/kyc")]
public class AdminKycController : ControllerBase
{
    readonly IKycAdminService _kycAdmin;
    readonly AdminOptions _admin;

    public AdminKycController(IKycAdminService kycAdmin, IOptions<AdminOptions> admin)
    {
        _kycAdmin = kycAdmin;
        _admin = admin.Value;
    }

    bool IsAuthorized()
    {
        if (string.IsNullOrWhiteSpace(_admin.ApiKey)) return false;
        if (!Request.Headers.TryGetValue("X-Admin-Key", out var key)) return false;
        return string.Equals(key.ToString(), _admin.ApiKey, StringComparison.Ordinal);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> ListPending([FromQuery] int page = 1)
    {
        if (!IsAuthorized()) return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        return Ok(await _kycAdmin.ListPendingAsync(page));
    }

    [HttpPost("{playerId:guid}/review")]
    public async Task<IActionResult> Review(Guid playerId, [FromBody] KycReviewRequest req)
    {
        if (!IsAuthorized()) return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var result = await _kycAdmin.ReviewAsync(playerId, req);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
