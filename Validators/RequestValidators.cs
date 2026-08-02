using FluentValidation;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.ContactEmail) || !string.IsNullOrWhiteSpace(x.ContactPhone))
            .WithMessage("Email or phone is required.");

        RuleFor(x => x.ContactEmail)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ContactEmail))
            .WithMessage("Invalid email format.");

        RuleFor(x => x.ContactPhone)
            .Matches(@"^[6-9]\d{9}$").When(x => !string.IsNullOrWhiteSpace(x.ContactPhone))
            .WithMessage("Phone must be a valid 10-digit Indian mobile number.");

        RuleFor(x => x.Password)
            .NotEmpty().MinimumLength(8).MaximumLength(128)
            .WithMessage("Password must be between 8 and 128 characters.");

        RuleFor(x => x.IsAgeVerified).Equal(true)
            .WithMessage("Age verification is required.");

        RuleFor(x => x.IsTermsAccepted).Equal(true)
            .WithMessage("Terms and conditions must be accepted.");

        RuleFor(x => x.StateCode)
            .MaximumLength(10).When(x => !string.IsNullOrWhiteSpace(x.StateCode));
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Contact).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public class DepositRequestValidator : AbstractValidator<DepositRequest>
{
    public DepositRequestValidator()
    {
        RuleFor(x => x.AmountPaise).GreaterThanOrEqualTo(5000);
        RuleFor(x => x.RazorpayOrderId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RazorpayPaymentId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RazorpaySignature).NotEmpty().MaximumLength(500);
    }
}

public class CreateDepositOrderRequestValidator : AbstractValidator<CreateDepositOrderRequest>
{
    public CreateDepositOrderRequestValidator()
    {
        RuleFor(x => x.AmountPaise).GreaterThanOrEqualTo(5000)
            .WithMessage("Minimum deposit is ₹50 (5000 paise).");
    }
}

public class AllocateLevelRequestValidator : AbstractValidator<AllocateLevelRequest>
{
    private static readonly HashSet<string> ValidTimeModes = ["1min", "2min", "3min", "4min", "5min"];
    private static readonly HashSet<string> ValidRewardModes = ["3x", "5x"];

    public AllocateLevelRequestValidator()
    {
        RuleFor(x => x.TimeMode)
            .NotEmpty()
            .Must(m => ValidTimeModes.Contains(m))
            .WithMessage("TimeMode must be one of: 1min, 2min, 3min, 4min, 5min.");

        RuleFor(x => x.RewardMode)
            .NotEmpty()
            .Must(m => ValidRewardModes.Contains(m))
            .WithMessage("RewardMode must be 3x or 5x.");
    }
}

public class SubmitResultRequestValidator : AbstractValidator<SubmitResultRequest>
{
    public SubmitResultRequestValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.SolveSecs).GreaterThanOrEqualTo(0).LessThanOrEqualTo(3600);
        RuleFor(x => x.TotalMoves).GreaterThanOrEqualTo(0);
    }
}

public class JoinMatchRequestValidator : AbstractValidator<JoinMatchRequest>
{
    private static readonly HashSet<int> ValidTimeSecs = [60, 120, 180, 240, 300];

    public JoinMatchRequestValidator()
    {
        RuleFor(x => x.EntryFeePaise).GreaterThan(0);
        RuleFor(x => x.TimeSecs)
            .Must(t => ValidTimeSecs.Contains(t))
            .WithMessage("TimeSecs must be 60, 120, 180, 240, or 300.");
        RuleFor(x => x.SignalRConnId).NotEmpty().MaximumLength(200);
    }
}

public class StartSinglePlayerRequestValidator : AbstractValidator<StartSinglePlayerRequest>
{
    public StartSinglePlayerRequestValidator()
    {
        RuleFor(x => x.TimeMode).NotEmpty();
        RuleFor(x => x.RewardMode).NotEmpty().Must(r => r is "3x" or "5x");
        RuleFor(x => x.EntryFeePaise).GreaterThan(0);
    }
}

public class StartFreePlayRequestValidator : AbstractValidator<StartFreePlayRequest>
{
    public StartFreePlayRequestValidator()
    {
        RuleFor(x => x.TimeMode).NotEmpty();
    }
}

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Contact).NotEmpty().MaximumLength(150);
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Contact).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ResetCode).NotEmpty().Length(6);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public class WithdrawRequestValidator : AbstractValidator<WithdrawRequest>
{
    public WithdrawRequestValidator() => RuleFor(x => x.AmountPaise).GreaterThanOrEqualTo(10000);
}
