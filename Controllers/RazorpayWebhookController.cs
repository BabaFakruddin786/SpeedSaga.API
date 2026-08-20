using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/webhooks")]
public class RazorpayWebhookController : ControllerBase
{
    private readonly IWalletService _wallet;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RazorpayWebhookController> _logger;

    public RazorpayWebhookController(IWalletService wallet, IConfiguration configuration, ILogger<RazorpayWebhookController> logger)
    {
        _wallet = wallet;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("razorpay")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleRazorpayWebhook()
    {
        var secret = _configuration["Razorpay:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("Razorpay webhook received but WebhookSecret is not configured.");
            return BadRequest(new ApiResponse<object>(false, "Webhook not configured."));
        }

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature) || !VerifyWebhookSignature(body, signature, secret))
            return Unauthorized(new ApiResponse<object>(false, "Invalid webhook signature."));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var eventName = root.GetProperty("event").GetString();

        if (eventName == "payment.captured")
        {
            var payment = root.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var orderId = payment.GetProperty("order_id").GetString();
            var paymentId = payment.GetProperty("id").GetString();
            var amountPaise = payment.GetProperty("amount").GetInt64();
            var playerIdStr = payment.TryGetProperty("notes", out var notes) && notes.TryGetProperty("playerId", out var pid)
                ? pid.GetString()
                : null;

            if (string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(paymentId))
                return Ok(new ApiResponse<object>(true, "Ignored — missing ids."));

            if (!Guid.TryParse(playerIdStr, out var playerId))
            {
                _logger.LogWarning("Webhook payment.captured without playerId note for order {OrderId}", orderId);
                return Ok(new ApiResponse<object>(true, "Ignored — no player mapping."));
            }

            var result = await _wallet.ProcessDepositFromWebhookAsync(playerId, amountPaise, orderId, paymentId);
            if (!result.Success)
                _logger.LogWarning("Webhook deposit failed for {OrderId}: {Message}", orderId, result.Message);
        }

        return Ok(new ApiResponse<object>(true, "Webhook processed."));
    }

    static bool VerifyWebhookSignature(string body, string signature, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return expected == signature.ToLowerInvariant();
    }
}
