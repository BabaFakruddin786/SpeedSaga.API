using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Authorization;
using SpeedSaga.API.Extensions;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/player/support")]
[Authorize(Policy = Policies.PlayerOnly)]
public class SupportController : ControllerBase
{
    readonly ISupportService _support;

    public SupportController(ISupportService support) => _support = support;

    [HttpGet("conversation")]
    public async Task<IActionResult> GetConversation()
        => Ok(await _support.GetConversationAsync(User.GetPlayerId()));

    [HttpPost("escalate")]
    public async Task<IActionResult> Escalate([FromBody] SupportMessageRequest req)
    {
        var result = await _support.EscalateAsync(User.GetPlayerId(), req.Message);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("messages")]
    public async Task<IActionResult> SendMessage([FromBody] SupportMessageRequest req)
    {
        var result = await _support.SendPlayerMessageAsync(User.GetPlayerId(), req.Message);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

[ApiController]
[Route("api/admin/support")]
public class AdminSupportController : ControllerBase
{
    readonly ISupportService _support;
    readonly ISupportConfigService _supportConfig;
    readonly ITickerConfigService _tickerConfig;
    readonly AdminOptions _admin;

    public AdminSupportController(
        ISupportService support,
        ISupportConfigService supportConfig,
        ITickerConfigService tickerConfig,
        IOptions<AdminOptions> admin)
    {
        _support = support;
        _supportConfig = supportConfig;
        _tickerConfig = tickerConfig;
        _admin = admin.Value;
    }

    bool IsAdminAuthorized() => AdminAuthorization.IsAuthorized(Request, _admin);

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig()
    {
        if (!IsAdminAuthorized()) return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var config = await _supportConfig.GetPublicConfigAsync();
        return config == null ? NotFound(new ApiResponse<object>(false, "Not configured")) : Ok(config);
    }

    [HttpPut("config")]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdateSupportConfigRequest req)
    {
        if (!IsAdminAuthorized()) return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var result = await _supportConfig.UpdateConfigAsync(req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("ticker-config")]
    public async Task<IActionResult> GetTickerConfig()
    {
        if (!IsAdminAuthorized()) return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var config = await _tickerConfig.GetPublicConfigAsync();
        return config == null ? NotFound(new ApiResponse<object>(false, "Not configured")) : Ok(config);
    }

    [HttpPut("ticker-config")]
    public async Task<IActionResult> UpdateTickerConfig([FromBody] UpdateTickerConfigRequest req)
    {
        if (!IsAdminAuthorized()) return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var result = await _tickerConfig.UpdateConfigAsync(req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("tickets")]
    public async Task<IActionResult> ListTickets([FromQuery] string? status)
    {
        if (!IsAdminAuthorized()) return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        return Ok(await _support.ListTicketsAsync(status));
    }

    [HttpGet("tickets/{ticketId:guid}")]
    public async Task<IActionResult> GetTicket(Guid ticketId)
    {
        if (!IsAdminAuthorized()) return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var ticket = await _support.GetTicketDetailAsync(ticketId);
        return ticket == null ? NotFound(new ApiResponse<object>(false, "Ticket not found")) : Ok(ticket);
    }

    [HttpPost("tickets/{ticketId:guid}/reply")]
    public async Task<IActionResult> Reply(Guid ticketId, [FromBody] SupportMessageRequest req)
    {
        if (!IsAdminAuthorized()) return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var result = await _support.AdminReplyAsync(ticketId, req.Message);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("tickets/{ticketId:guid}/close")]
    public async Task<IActionResult> Close(Guid ticketId)
    {
        if (!IsAdminAuthorized()) return Unauthorized(new ApiResponse<object>(false, "Invalid admin key"));
        var result = await _support.CloseTicketAsync(ticketId);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
