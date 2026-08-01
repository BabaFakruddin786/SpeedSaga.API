using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Extensions;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/wallet")]
[Authorize(Policy = Authorization.Policies.PlayerOnly)]
public class WalletController : ControllerBase
{
    private readonly IWalletService _wallet;
    private readonly IRazorpayService _razorpay;

    public WalletController(IWalletService wallet, IRazorpayService razorpay)
    {
        _wallet = wallet;
        _razorpay = razorpay;
    }

    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance()
    {
        var data = await _wallet.GetWalletAsync(User.GetPlayerId());
        return data == null ? NotFound(new ApiResponse<object>(false, "Wallet not found.")) : Ok(data);
    }

    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateDepositOrderRequest req)
    {
        var orderId = await _razorpay.CreateOrderAsync(req.AmountPaise);
        return Ok(new ApiResponse<object>(true, "Order created", new { OrderId = orderId, AmountPaise = req.AmountPaise }));
    }

    [HttpPost("deposit")]
    public async Task<IActionResult> Deposit([FromBody] DepositRequest req)
    {
        var result = await _wallet.ProcessDepositAsync(User.GetPlayerId(), req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions([FromQuery] string? type, [FromQuery] int page = 1)
        => Ok(await _wallet.GetTransactionsAsync(User.GetPlayerId(), type, page));
}
