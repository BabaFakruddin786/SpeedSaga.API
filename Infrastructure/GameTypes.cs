namespace SpeedSaga.API.Infrastructure;

public static class GameTypes
{
    public const string Arrow = "arrow";
    public const string CarParking = "car_parking";
    public const string TicTacToe = "tic_tac_toe";

    public static bool IsValid(string? gameType)
    {
        if (string.IsNullOrWhiteSpace(gameType)) return true;
        return gameType is Arrow or CarParking or TicTacToe;
    }

    public static string Normalize(string? gameType) =>
        string.IsNullOrWhiteSpace(gameType) ? Arrow : gameType.Trim().ToLowerInvariant();
}
