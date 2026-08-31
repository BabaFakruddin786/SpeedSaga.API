using System.Security.Claims;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Authorization;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Infrastructure;

public static class AdminAuthorization
{
    public static bool TryAuthorizeApiKey(HttpRequest request, AdminOptions admin, out ClaimsPrincipal? principal)
    {
        principal = null;
        if (string.IsNullOrWhiteSpace(admin.ApiKey)) return false;
        if (!request.Headers.TryGetValue("X-Admin-Key", out var key)) return false;
        if (!string.Equals(key.ToString(), admin.ApiKey, StringComparison.Ordinal)) return false;

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, AppRoles.SuperAdmin) }, "AdminKey");
        principal = new ClaimsPrincipal(identity);
        return true;
    }

    public static ClaimsPrincipal GetEffectiveUser(HttpContext ctx, AdminOptions admin)
    {
        if (ctx.User.Identity?.IsAuthenticated == true &&
            (ctx.User.IsInRole(AppRoles.SuperAdmin) || ctx.User.IsInRole(AppRoles.Support)))
            return ctx.User;

        if (TryAuthorizeApiKey(ctx.Request, admin, out var principal) && principal != null)
            return principal;

        return ctx.User;
    }

    public static bool HasAdminAccess(HttpContext ctx, AdminOptions admin)
    {
        var user = GetEffectiveUser(ctx, admin);
        return user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.Support);
    }

    public static bool HasSuperAdminAccess(HttpContext ctx, AdminOptions admin)
    {
        var user = GetEffectiveUser(ctx, admin);
        return user.IsInRole(AppRoles.SuperAdmin);
    }

    public static bool IsAuthorized(HttpRequest request, AdminOptions admin)
        => TryAuthorizeApiKey(request, admin, out _);
}
