using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public record OtpSendRequest(
    Guid? PlayerId,
    string Purpose,
    string Channel,
    string Destination,
    string? ContextJson = null,
    bool IncludeDevOtpInResponse = false);

public record OtpSendResult(
    bool Success,
    string Message,
    string? RefId = null,
    string? DevOtp = null,
    int? RetryAfterSeconds = null);

public record OtpVerifyRequest(
    string Otp,
    string? RefId = null,
    Guid? PlayerId = null,
    string? Destination = null,
    string? Purpose = null);

public record OtpVerifyResult(bool Success, string Message, string? ContextJson = null);

public interface IOtpService
{
    Task<OtpSendResult> SendAsync(OtpSendRequest request, CancellationToken ct = default);
    Task<OtpVerifyResult> VerifyAsync(OtpVerifyRequest request, CancellationToken ct = default);
    Task CleanupExpiredSessionsAsync();
}

public class OtpService : IOtpService
{
    readonly ISqlConnectionFactory _db;
    readonly MessagingOptions _opts;
    readonly IMessageDispatchQueue _dispatch;
    readonly IWebHostEnvironment _env;
    readonly ILogger<OtpService> _log;

    public OtpService(
        ISqlConnectionFactory db,
        IOptions<MessagingOptions> opts,
        IMessageDispatchQueue dispatch,
        IWebHostEnvironment env,
        ILogger<OtpService> log)
    {
        _db = db;
        _opts = opts.Value;
        _dispatch = dispatch;
        _env = env;
        _log = log;
    }

    public async Task<OtpSendResult> SendAsync(OtpSendRequest request, CancellationToken ct = default)
    {
        var destination = request.Destination.Trim();
        var channel = request.Channel;
        var purpose = request.Purpose;

        if (string.IsNullOrWhiteSpace(destination))
            return new OtpSendResult(false, "Destination is required");

        var rate = await CheckRateLimitAsync(request.PlayerId, destination, purpose, ct);
        if (!rate.Allowed)
            return new OtpSendResult(false, rate.Message, RetryAfterSeconds: rate.RetryAfterSeconds);

        var otp = _env.IsDevelopment() ? "123456" : OtpSecurity.GenerateOtp(_opts.Otp.Length);
        var salt = OtpSecurity.GenerateSalt();
        var hash = OtpSecurity.HashOtp(otp, salt);
        var expiresAt = DateTime.UtcNow.AddMinutes(_opts.Otp.ExpiryMinutes);
        var mask = OtpSecurity.MaskDestination(destination, channel);
        var bodyPreview = $"Your SpeedSaga OTP is ******. Valid for {_opts.Otp.ExpiryMinutes} minutes.";

        // Context for post-verify (masked aadhaar etc.) — never store plaintext OTP in DB
        var contextJson = request.ContextJson;

        var provider = channel == MessageChannels.Email ? _opts.EmailProvider : _opts.SmsProvider;

        Guid messageId, sessionId;
        await using (var cn = _db.CreateConnection())
        {
            await cn.OpenAsync(ct);

            await using var msgCmd = new SqlCommand("USP_InsertOutgoingMessage", cn) { CommandType = CommandType.StoredProcedure };
            var pMsgId = msgCmd.Parameters.Add("@MessageId", SqlDbType.UniqueIdentifier);
            pMsgId.Direction = ParameterDirection.Output;
            msgCmd.Parameters.AddWithValue("@PlayerId", (object?)request.PlayerId ?? DBNull.Value);
            msgCmd.Parameters.AddWithValue("@Channel", channel);
            msgCmd.Parameters.AddWithValue("@Purpose", purpose);
            msgCmd.Parameters.AddWithValue("@Destination", destination);
            msgCmd.Parameters.AddWithValue("@DestinationMask", mask);
            msgCmd.Parameters.AddWithValue("@BodyPreview", bodyPreview);
            msgCmd.Parameters.AddWithValue("@Provider", provider);
            msgCmd.Parameters.AddWithValue("@OtpSessionId", DBNull.Value);
            await msgCmd.ExecuteNonQueryAsync(ct);
            messageId = (Guid)pMsgId.Value!;

            await using var sessCmd = new SqlCommand("USP_CreateOtpSession", cn) { CommandType = CommandType.StoredProcedure };
            var pSid = sessCmd.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier);
            pSid.Direction = ParameterDirection.Output;
            sessCmd.Parameters.AddWithValue("@PlayerId", (object?)request.PlayerId ?? DBNull.Value);
            sessCmd.Parameters.AddWithValue("@Purpose", purpose);
            sessCmd.Parameters.AddWithValue("@Channel", channel);
            sessCmd.Parameters.AddWithValue("@Destination", destination);
            sessCmd.Parameters.AddWithValue("@OtpHash", hash);
            sessCmd.Parameters.AddWithValue("@OtpSalt", salt);
            sessCmd.Parameters.AddWithValue("@ContextJson", (object?)contextJson ?? DBNull.Value);
            sessCmd.Parameters.AddWithValue("@ExpiresAt", expiresAt);
            sessCmd.Parameters.AddWithValue("@MaxAttempts", _opts.Otp.MaxVerifyAttempts);
            sessCmd.Parameters.AddWithValue("@MessageId", messageId);
            await sessCmd.ExecuteNonQueryAsync(ct);
            sessionId = (Guid)pSid.Value!;

            await using var linkCmd = new SqlCommand(
                "UPDATE OutgoingMessages SET OtpSessionId = @Sid WHERE MessageId = @Mid", cn);
            linkCmd.Parameters.AddWithValue("@Sid", sessionId);
            linkCmd.Parameters.AddWithValue("@Mid", messageId);
            await linkCmd.ExecuteNonQueryAsync(ct);
        }

        _dispatch.Enqueue(messageId, otp);

        var refId = sessionId.ToString("N");
        var userMsg = channel == MessageChannels.Email
            ? "OTP sent to your email address"
            : "OTP sent to your mobile number";

        if (_env.IsDevelopment())
            userMsg += " (dev: use 123456)";

        return new OtpSendResult(
            true,
            userMsg,
            RefId: refId,
            DevOtp: request.IncludeDevOtpInResponse && _env.IsDevelopment() ? otp : null);
    }

    public async Task<OtpVerifyResult> VerifyAsync(OtpVerifyRequest request, CancellationToken ct = default)
    {
        var otp = request.Otp?.Trim() ?? "";
        if (otp.Length != _opts.Otp.Length || !otp.All(char.IsDigit))
            return new OtpVerifyResult(false, $"Enter the {_opts.Otp.Length}-digit OTP");

        Guid? sessionId = null;
        if (!string.IsNullOrWhiteSpace(request.RefId) && Guid.TryParse(request.RefId, out var parsed))
            sessionId = parsed;

        // Hash lookup requires salt from session — verify via SP with hash computed after load
        // We pass hash computed from a dummy approach: SP compares stored hash
        // Need to get salt first OR pass otp to SP. SP currently takes hash only.
        // Load session salt first for hash, then call SP.

        string? salt = null;
        await using (var cn = _db.CreateConnection())
        {
            await cn.OpenAsync(ct);
            await using var cmd = sessionId.HasValue
                ? new SqlCommand("SELECT OtpSalt FROM OtpSessions WHERE SessionId = @Id", cn)
                : new SqlCommand(@"SELECT TOP 1 OtpSalt FROM OtpSessions
                    WHERE Purpose = @Purpose AND Destination = @Dest AND IsVerified = 0 AND IsRevoked = 0
                    ORDER BY CreatedAt DESC", cn);
            if (sessionId.HasValue)
                cmd.Parameters.AddWithValue("@Id", sessionId.Value);
            else
            {
                cmd.Parameters.AddWithValue("@Purpose", request.Purpose ?? "");
                cmd.Parameters.AddWithValue("@Dest", request.Destination ?? "");
            }
            salt = await cmd.ExecuteScalarAsync(ct) as string;
        }

        if (string.IsNullOrWhiteSpace(salt))
            return new OtpVerifyResult(false, "OTP expired or invalid. Request a new OTP.");

        var hash = OtpSecurity.HashOtp(otp, salt);

        await using var cn2 = _db.CreateConnection();
        await cn2.OpenAsync(ct);
        await using var verifyCmd = new SqlCommand("USP_VerifyOtpSession", cn2) { CommandType = CommandType.StoredProcedure };
        verifyCmd.Parameters.AddWithValue("@SessionId", (object?)sessionId ?? DBNull.Value);
        verifyCmd.Parameters.AddWithValue("@PlayerId", (object?)request.PlayerId ?? DBNull.Value);
        verifyCmd.Parameters.AddWithValue("@Destination", (object?)request.Destination ?? DBNull.Value);
        verifyCmd.Parameters.AddWithValue("@Purpose", (object?)request.Purpose ?? DBNull.Value);
        verifyCmd.Parameters.AddWithValue("@OtpHash", hash);
        var pRes = verifyCmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;
        var pMsg = verifyCmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;
        var pCtx = verifyCmd.Parameters.Add("@ContextJson", SqlDbType.NVarChar, 1000);
        pCtx.Direction = ParameterDirection.Output;
        await verifyCmd.ExecuteNonQueryAsync(ct);

        var result = (int)pRes.Value!;
        var message = (string)pMsg.Value!;
        var ctx = pCtx.Value == DBNull.Value ? null : (string)pCtx.Value!;

        return result == 1
            ? new OtpVerifyResult(true, message, ctx)
            : new OtpVerifyResult(false, message);
    }

    async Task<(bool Allowed, string Message, int? RetryAfterSeconds)> CheckRateLimitAsync(
        Guid? playerId, string destination, string purpose, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_CheckOtpRateLimit", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", (object?)playerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Destination", destination);
        cmd.Parameters.AddWithValue("@Purpose", purpose);
        cmd.Parameters.AddWithValue("@WindowMinutes", _opts.Otp.SendWindowMinutes);
        cmd.Parameters.AddWithValue("@MaxSends", _opts.Otp.MaxSendPerWindow);
        cmd.Parameters.AddWithValue("@CooldownSeconds", _opts.Otp.ResendCooldownSeconds);
        var pAllowed = cmd.Parameters.Add("@Allowed", SqlDbType.Bit);
        pAllowed.Direction = ParameterDirection.Output;
        var pRetry = cmd.Parameters.Add("@RetryAfterSeconds", SqlDbType.Int);
        pRetry.Direction = ParameterDirection.Output;
        var pMsg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);

        return ((bool)pAllowed.Value!, (string)pMsg.Value!, (int)pRetry.Value! == 0 ? null : (int?)pRetry.Value);
    }

    public async Task CleanupExpiredSessionsAsync()
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_CleanupExpiredOtpSessions", cn) { CommandType = CommandType.StoredProcedure };
        await cmd.ExecuteNonQueryAsync();
    }
}
