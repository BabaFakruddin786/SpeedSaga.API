using System.Security.Claims;
using SpeedSaga.API.Authorization;

namespace SpeedSaga.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetPlayerId(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst(AppClaimTypes.PlayerId)?.Value
            ?? throw new UnauthorizedAccessException("Player ID claim is missing.");
        return Guid.Parse(claim);
    }
}
