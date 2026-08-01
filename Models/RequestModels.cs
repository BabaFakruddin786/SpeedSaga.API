namespace SpeedSaga.API.Models;

public record RegisterRequest(
    string? ContactEmail,
    string? ContactPhone,
    string Password,
    string? ReferralCode,
    string? StateCode,
    bool IsAgeVerified,
    bool IsTermsAccepted);

public record LoginRequest(string Contact, string Password);

public record DepositRequest(
    long AmountPaise,
    string RazorpayOrderId,
    string RazorpayPaymentId,
    string RazorpaySignature);

public record AllocateLevelRequest(string TimeMode, string RewardMode);

public record SubmitResultRequest(
    Guid SessionId,
    bool IsWon,
    int SolveSecs,
    string? MovesJson,
    int TotalMoves);

public record JoinMatchRequest(
    long EntryFeePaise,
    int TimeSecs,
    string SignalRConnId);

public record CreateDepositOrderRequest(long AmountPaise);

public record StartSinglePlayerRequest(
    string TimeMode,
    string RewardMode,
    long EntryFeePaise);

public record AllocatedLevelResult(int LevelId, string GridJson);

public record ApiResponse<T>(bool Success, string Message, T? Data = default);
