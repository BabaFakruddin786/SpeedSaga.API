namespace SpeedSaga.API.Infrastructure;

/// <summary>State restrictions apply only to withdrawals — not gameplay or deposits.</summary>
public static class StateGeoblock
{
    static readonly HashSet<string> RestrictedStates =
        new(StringComparer.OrdinalIgnoreCase) { "TG", "AP", "TN", "KL", "SK", "MG", "NL" };

    public const string Message = "Real-money gaming is not available in your state due to local regulations.";

    public static bool IsRestricted(string? stateCode)
        => !string.IsNullOrWhiteSpace(stateCode) && RestrictedStates.Contains(stateCode.Trim());
}
