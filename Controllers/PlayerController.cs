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
    private readonly IWebHostEnvironment _env;

    public PlayerController(IPlayerService player, IKycVerificationService kycVerify, IOutgoingMessageService messages, IWebHostEnvironment env)
    {
        _player = player;
        _kycVerify = kycVerify;
        _messages = messages;
        _env = env;
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

    [HttpPut("appearance")]
    public async Task<IActionResult> SetAppearance([FromBody] SetAppearanceRequest req)
    {
        var result = await _player.SetAppearanceAsync(User.GetPlayerId(), req.AppearanceMode);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("kyc")]
    public async Task<IActionResult> GetKyc() => Ok(await _player.GetKycAsync(User.GetPlayerId()));

    [HttpPost("kyc/aadhaar/otp")]
    public async Task<IActionResult> SendAadhaarOtp([FromBody] AadhaarOtpSendRequest req)
    {
        var result = await _kycVerify.SendAadhaarOtpAsync(User.GetPlayerId(), req.AadhaarNumber);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("kyc/aadhaar/verify")]
    public async Task<IActionResult> VerifyAadhaarOtp([FromBody] AadhaarOtpVerifyRequest req)
    {
        var result = await _kycVerify.VerifyAadhaarOtpAsync(User.GetPlayerId(), req.RefId, req.Otp);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("kyc/pan/verify")]
    public async Task<IActionResult> VerifyPan([FromBody] PanVerifyRequest req)
    {
        var result = await _kycVerify.VerifyPanAsync(User.GetPlayerId(), req.PanNumber);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("kyc/bank/verify")]
    public async Task<IActionResult> VerifyBank([FromBody] BankVerifyRequest req)
    {
        var result = await _kycVerify.VerifyBankAsync(User.GetPlayerId(), req.AccountNumber, req.Ifsc, req.HolderName);
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
}
