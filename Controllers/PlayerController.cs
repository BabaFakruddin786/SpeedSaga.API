using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Extensions;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/player")]
[Authorize(Policy = Authorization.Policies.PlayerOnly)]
public class PlayerController : ControllerBase
{
    private readonly IPlayerService _player;
    private readonly IKycVerificationService _kycVerify;
    private readonly IOutgoingMessageService _messages;
    private readonly IOtpService _otp;
    private readonly IWebHostEnvironment _env;
    private readonly ILogViewerService _logs;

    public PlayerController(IPlayerService player, IKycVerificationService kycVerify, IOutgoingMessageService messages,
        IOtpService otp, IWebHostEnvironment env, ILogViewerService logs)
    {
        _player = player;
        _kycVerify = kycVerify;
        _messages = messages;
        _otp = otp;
        _env = env;
        _logs = logs;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var data = await _player.GetDashboardAsync(User.GetPlayerId());
        return data == null
            ? NotFound(new ApiResponse<object>(false, "Player not found."))
            : Ok(data);
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        var result = await _player.UpdateProfileAsync(User.GetPlayerId(), req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("profile/send-otp")]
    public async Task<IActionResult> SendProfileOtp([FromBody] ProfileOtpSendRequest req)
    {
        var contactType = req.ContactType?.Trim().ToLowerInvariant() ?? "";
        var destination = req.Destination?.Trim() ?? "";
        if (contactType is not ("email" or "phone"))
            return BadRequest(new ApiResponse<object>(false, "ContactType must be email or phone"));
        if (string.IsNullOrWhiteSpace(destination))
            return BadRequest(new ApiResponse<object>(false, "Destination is required"));

        var channel = contactType == "email" ? MessageChannels.Email : MessageChannels.Sms;
        if (channel == MessageChannels.Email)
            destination = destination.ToLowerInvariant();

        var result = await _otp.SendAsync(new OtpSendRequest(
            User.GetPlayerId(),
            OtpPurposes.LinkContact,
            channel,
            destination,
            IncludeDevOtpInResponse: _env.IsDevelopment()));

        return result.Success
            ? Ok(new ApiResponse<object>(true, result.Message, new { refId = result.RefId, devOtp = result.DevOtp }))
            : BadRequest(new ApiResponse<object>(false, result.Message));
    }

    [HttpPut("appearance")]
    public async Task<IActionResult> SetAppearance([FromBody] SetAppearanceRequest req)
    {
        var result = await _player.SetAppearanceAsync(User.GetPlayerId(), req.AppearanceMode);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("kyc")]
    public async Task<IActionResult> GetKyc() => Ok(await _player.GetKycAsync(User.GetPlayerId()));

    [HttpPost("kyc/aadhaar/submit")]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> SubmitAadhaar([FromForm] string aadhaarNumber, [FromForm] string nameOnAadhaar, IFormFile photo)
    {
        var result = await _kycVerify.SubmitAadhaarAsync(User.GetPlayerId(), aadhaarNumber, nameOnAadhaar, photo);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("kyc/pan/submit")]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> SubmitPan([FromForm] string panNumber, [FromForm] IFormFile photo)
    {
        var result = await _kycVerify.SubmitPanAsync(User.GetPlayerId(), panNumber, photo);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("kyc/bank/submit")]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> SubmitBank([FromForm] string accountNumber, [FromForm] string ifsc, [FromForm] string holderName, IFormFile photo)
    {
        var result = await _kycVerify.SubmitBankAsync(User.GetPlayerId(), accountNumber, ifsc, holderName, photo);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("kyc")]
    public async Task<IActionResult> SubmitKyc([FromBody] KycSubmitRequest req)
    {
        var result = await _player.SubmitKycAsync(User.GetPlayerId(), req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("kyc/dev-approve")]
    public async Task<IActionResult> DevApproveKyc()
    {
        if (!_env.IsDevelopment()) return Forbid();
        var result = await _player.DevApproveKycAsync(User.GetPlayerId());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages([FromQuery] int page = 1)
        => Ok(await _messages.GetPlayerMessagesAsync(User.GetPlayerId(), page));

    [HttpPost("logs/upload")]
    public async Task<IActionResult> UploadLogs([FromBody] ClientLogUploadRequest req, CancellationToken ct)
    {
        if (req.Lines == null || req.Lines.Count == 0)
            return BadRequest(new ApiResponse<object>(false, "No log lines provided."));
        try
        {
            var count = await _logs.AppendAppUploadAsync(User.GetPlayerId(), req.Lines, ct);
            return Ok(new ApiResponse<object>(true, $"Stored {count} log line(s).", new { count }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object>(false, ex.Message));
        }
    }
}

public record ClientLogUploadRequest(IReadOnlyList<string> Lines);
