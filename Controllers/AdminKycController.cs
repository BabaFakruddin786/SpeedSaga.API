using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
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

    [HttpGet("pending")]
    public async Task<IActionResult> ListPending([FromQuery] int page = 1)
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        return Ok(await _kycAdmin.ListPendingAsync(page));
    }

    [HttpPost("{playerId:guid}/review")]
    public async Task<IActionResult> Review(Guid playerId, [FromBody] KycReviewRequest req)
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var result = await _kycAdmin.ReviewAsync(playerId, req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("{playerId:guid}/documents/{docType}")]
    public async Task<IActionResult> GetDocument(Guid playerId, string docType)
    {
        if (!AdminAuthorization.IsAuthorized(Request, _admin))
            return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var doc = await _kycAdmin.GetDocumentAsync(playerId, docType);
        return doc == null
            ? NotFound(new ApiResponse<object>(false, "Document not found"))
            : PhysicalFile(doc.Value.Path, doc.Value.ContentType);
    }
}
