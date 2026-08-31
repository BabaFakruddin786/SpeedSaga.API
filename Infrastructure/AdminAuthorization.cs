using Microsoft.Extensions.Options;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Infrastructure;

public static class AdminAuthorization
{
    public static bool IsAuthorized(HttpRequest request, AdminOptions admin)
    {
        if (string.IsNullOrWhiteSpace(admin.ApiKey)) return false;
        if (!request.Headers.TryGetValue("X-Admin-Key", out var key)) return false;
        return string.Equals(key.ToString(), admin.ApiKey, StringComparison.Ordinal);
    }
}
