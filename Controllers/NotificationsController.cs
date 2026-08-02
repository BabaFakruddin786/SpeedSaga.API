using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeedSaga.API.Extensions;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;

namespace SpeedSaga.API.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Policy = Authorization.Policies.PlayerOnly)]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1)
        => Ok(await _notifications.GetNotificationsAsync(User.GetPlayerId(), page));

    [HttpPost("{notifId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid notifId)
    {
        await _notifications.MarkReadAsync(User.GetPlayerId(), notifId);
        return Ok(new ApiResponse<object>(true, "Notification marked as read."));
    }
}
