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

public record AllocateLevelRequest(string TimeMode, string RewardMode, long EntryFeePaise = 0);

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

public record StartFreePlayRequest(string TimeMode);

public record ForgotPasswordRequest(string Contact, string? Channel = null);

public record ResetPasswordRequest(string Contact, string ResetCode, string NewPassword, string? RefId = null);

public record WithdrawRequest(long AmountPaise);

public record UpdateProfileRequest(string? Username, string? StateCode, string? ContactEmail = null, string? ContactPhone = null);
public record SetAppearanceRequest(string AppearanceMode);

public record KycSubmitRequest(string DocType, string DocNumber, string? HolderName);

public record AadhaarOtpSendRequest(string AadhaarNumber);

public record AadhaarOtpVerifyRequest(string RefId, string Otp);

public record PanVerifyRequest(string PanNumber);

public record BankVerifyRequest(string AccountNumber, string Ifsc, string HolderName);

public record DevDepositRequest(long AmountPaise);

public record SyncMoveRequest(string SessionId, string Direction, int Col, int Row, float Timestamp);

public record AllocatedLevelResult(int LevelId, string GridJson, string PuzzleTier = "Easy", int TargetArrows = 30, int GridCols = 0, int GridRows = 0);

public record ApiResponse<T>(bool Success, string Message, T? Data = default);
