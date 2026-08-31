namespace SpeedSaga.API.Authorization;

public static class Policies
{
    public const string PlayerOnly = "PlayerOnly";
    public const string VerifiedPlayer = "VerifiedPlayer";
}

public static class AppClaimTypes
{
    public const string PlayerId = "playerId";
    public const string AdminUserId = "adminUserId";
    public const string Contact = "contact";
    public const string StateCode = "stateCode";
}

public static class AppRoles
{
    public const string Player = "Player";
    public const string SuperAdmin = "SuperAdmin";
    public const string Support = "Support";
}
