using System.Data;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IMessageDeliveryService
{
    Task DispatchAsync(Guid messageId, string? plainOtp = null, CancellationToken ct = default);
}

public class MessageDeliveryService : IMessageDeliveryService
{
    readonly ISqlConnectionFactory _db;
    readonly MessagingOptions _opts;
    readonly IHttpClientFactory _http;
    readonly IWebHostEnvironment _env;
    readonly ILogger<MessageDeliveryService> _log;

    public MessageDeliveryService(
        ISqlConnectionFactory db,
        IOptions<MessagingOptions> opts,
        IHttpClientFactory http,
        IWebHostEnvironment env,
        ILogger<MessageDeliveryService> log)
    {
        _db = db;
        _opts = opts.Value;
        _http = http;
        _env = env;
        _log = log;
    }

    public async Task DispatchAsync(Guid messageId, string? plainOtp = null, CancellationToken ct = default)
    {
        var msg = await LoadMessageAsync(messageId, ct);
        if (msg == null) return;

        await UpdateStatusAsync(messageId, MessageStatuses.Sending, null, ct);

        try
        {
            var (status, detail, providerRef) = msg.Channel switch
            {
                MessageChannels.Email => await SendEmailAsync(msg, plainOtp, ct),
                _ => await SendSmsAsync(msg, plainOtp, ct)
            };
            await UpdateStatusAsync(messageId, status, detail, ct, providerRef);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Delivery failed for message {MessageId}", messageId);
            await UpdateStatusAsync(messageId, MessageStatuses.Failed, ex.Message, ct);
        }
    }

    async Task<(string Status, string? Detail, string? ProviderRef)> SendSmsAsync(OutgoingMessageRow msg, string? plainOtp, CancellationToken ct)
    {
        var body = plainOtp != null
            ? $"Your SpeedSaga OTP is {plainOtp}. Valid for {_opts.Otp.ExpiryMinutes} minutes. Do not share."
            : msg.BodyPreview;

        if (_env.IsDevelopment() || string.Equals(_opts.SmsProvider, "Dev", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogInformation("SMS [{Purpose}] to {Dest}: {Body}", msg.Purpose, msg.DestinationMask, body);
            return (MessageStatuses.Sent, "Delivered via dev provider", "dev-sms");
        }

        if (string.Equals(_opts.SmsProvider, "Msg91", StringComparison.OrdinalIgnoreCase))
            return await SendViaMsg91Async(msg.Destination, body, ct);

        _log.LogWarning("Unknown SMS provider {Provider}; marking sent in log only", _opts.SmsProvider);
        return (MessageStatuses.Sent, $"Logged only ({_opts.SmsProvider})", null);
    }

    async Task<(string Status, string? Detail, string? ProviderRef)> SendViaMsg91Async(string mobile, string body, CancellationToken ct)
    {
        var cfg = _opts.Msg91;
        if (string.IsNullOrWhiteSpace(cfg.AuthKey))
            return (MessageStatuses.Failed, "MSG91 AuthKey not configured", null);

        var client = _http.CreateClient("Msg91");
        client.DefaultRequestHeaders.Remove("authkey");
        client.DefaultRequestHeaders.Add("authkey", cfg.AuthKey);

        var payload = new
        {
            template_id = cfg.TemplateId,
            short_url = "0",
            recipients = new[] { new { mobiles = NormalizeIndianMobile(mobile), var1 = body } }
        };

        using var response = await client.PostAsJsonAsync(cfg.BaseUrl, payload, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return (MessageStatuses.Failed, $"MSG91 HTTP {(int)response.StatusCode}: {responseText}", null);

        return (MessageStatuses.Sent, "MSG91 accepted", responseText.Length > 120 ? responseText[..120] : responseText);
    }

    async Task<(string Status, string? Detail, string? ProviderRef)> SendEmailAsync(OutgoingMessageRow msg, string? plainOtp, CancellationToken ct)
    {
        var subject = msg.Purpose switch
        {
            OtpPurposes.PasswordReset => "SpeedSaga password reset code",
            OtpPurposes.KycAadhaar => "SpeedSaga Aadhaar verification OTP",
            OtpPurposes.LinkContact => "SpeedSaga contact verification OTP",
            _ => "SpeedSaga verification code"
        };
        var body = plainOtp != null
            ? $"Your SpeedSaga OTP is {plainOtp}. Valid for {_opts.Otp.ExpiryMinutes} minutes. Do not share this code."
            : msg.BodyPreview;

        if (_env.IsDevelopment() || string.Equals(_opts.EmailProvider, "Dev", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogInformation("EMAIL [{Purpose}] to {Dest}: {Subject} — {Body}", msg.Purpose, msg.DestinationMask, subject, body);
            return (MessageStatuses.Sent, "Delivered via dev provider", "dev-email");
        }

        var smtp = _opts.Smtp;
        if (string.IsNullOrWhiteSpace(smtp.Host))
            return (MessageStatuses.Failed, "SMTP not configured", null);

        using var client = new System.Net.Mail.SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.Port == 587 || smtp.Port == 465,
            Credentials = string.IsNullOrWhiteSpace(smtp.Username)
                ? null
                : new System.Net.NetworkCredential(smtp.Username, smtp.Password)
        };
        using var mail = new System.Net.Mail.MailMessage(
            new System.Net.Mail.MailAddress(smtp.FromEmail, smtp.FromName),
            new System.Net.Mail.MailAddress(msg.Destination))
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        await client.SendMailAsync(mail, ct);
        return (MessageStatuses.Sent, "SMTP sent", null);
    }

    static string NormalizeIndianMobile(string phone)
    {
        phone = new string(phone.Where(char.IsDigit).ToArray());
        if (phone.Length == 10) return "91" + phone;
        if (phone.StartsWith("91") && phone.Length == 12) return phone;
        return phone;
    }

    async Task<OutgoingMessageRow?> LoadMessageAsync(Guid messageId, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_GetOutgoingMessageById", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@MessageId", messageId);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return null;
        return new OutgoingMessageRow
        {
            MessageId = (Guid)rdr["MessageId"],
            PlayerId = rdr["PlayerId"] == DBNull.Value ? null : (Guid)rdr["PlayerId"],
            Channel = rdr["Channel"].ToString()!,
            Purpose = rdr["Purpose"].ToString()!,
            Destination = rdr["Destination"].ToString()!,
            DestinationMask = rdr["DestinationMask"].ToString()!,
            BodyPreview = rdr["BodyPreview"].ToString()!,
            Provider = rdr["Provider"].ToString()!,
            OtpSessionId = rdr["OtpSessionId"] == DBNull.Value ? null : (Guid)rdr["OtpSessionId"]
        };
    }

    async Task UpdateStatusAsync(Guid messageId, string status, string? detail, CancellationToken ct, string? providerRef = null)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_UpdateOutgoingMessageStatus", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@MessageId", messageId);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@StatusDetail", (object?)detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ProviderRefId", (object?)providerRef ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    sealed class OutgoingMessageRow
    {
        public Guid MessageId { get; init; }
        public Guid? PlayerId { get; init; }
        public string Channel { get; init; } = "";
        public string Purpose { get; init; } = "";
        public string Destination { get; init; } = "";
        public string DestinationMask { get; init; } = "";
        public string BodyPreview { get; init; } = "";
        public string Provider { get; init; } = "";
        public Guid? OtpSessionId { get; init; }
    }
}
