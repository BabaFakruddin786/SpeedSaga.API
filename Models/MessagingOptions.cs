namespace SpeedSaga.API.Models;

public class MessagingOptions
{
    public const string SectionName = "Messaging";
    public string SmsProvider { get; set; } = "Dev";
    public string EmailProvider { get; set; } = "Dev";
    public Msg91Options Msg91 { get; set; } = new();
    public SmtpOptions Smtp { get; set; } = new();
    public OtpOptions Otp { get; set; } = new();
}

public class Msg91Options
{
    public string AuthKey { get; set; } = "";
    public string SenderId { get; set; } = "SPDSSG";
    public string Route { get; set; } = "4";
    public string TemplateId { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.msg91.com/api/v5/flow/";
}

public class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "noreply@speedsaga.com";
    public string FromName { get; set; } = "SpeedSaga";
}

public class OtpOptions
{
    public int Length { get; set; } = 6;
    public int ExpiryMinutes { get; set; } = 10;
    public int MaxSendPerWindow { get; set; } = 3;
    public int SendWindowMinutes { get; set; } = 15;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxVerifyAttempts { get; set; } = 5;
}

public static class OtpPurposes
{
    public const string KycAadhaar = "KycAadhaar";
    public const string PasswordReset = "PasswordReset";
    public const string LinkContact = "LinkContact";
}

public static class MessageChannels
{
    public const string Sms = "SMS";
    public const string Email = "Email";
}

public static class MessageStatuses
{
    public const string Queued = "Queued";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
}
