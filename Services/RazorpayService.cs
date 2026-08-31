using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpeedSaga.API.Services;

public interface IRazorpayService
{
    Task<bool> VerifySignature(string orderId, string paymentId, string signature);
    Task<string> CreateOrderAsync(long amountPaise, Guid playerId);
    Task<string> GetKeyIdAsync(CancellationToken ct = default);
}

public class RazorpayService : IRazorpayService
{
    readonly IPaymentConfigService _paymentConfig;
    readonly IHttpClientFactory _httpClientFactory;

    public RazorpayService(IPaymentConfigService paymentConfig, IHttpClientFactory httpClientFactory)
    {
        _paymentConfig = paymentConfig;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> GetKeyIdAsync(CancellationToken ct = default)
    {
        var secrets = await _paymentConfig.GetSecretsAsync(ct);
        return secrets.KeyId;
    }

    public async Task<bool> VerifySignature(string orderId, string paymentId, string signature)
    {
        var secrets = await _paymentConfig.GetSecretsAsync();
        if (string.IsNullOrWhiteSpace(secrets.KeySecret))
            return false;

        var payload = $"{orderId}|{paymentId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secrets.KeySecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return expected == signature.ToLowerInvariant();
    }

    public async Task<string> CreateOrderAsync(long amountPaise, Guid playerId)
    {
        var secrets = await _paymentConfig.GetSecretsAsync();
        if (!secrets.IsRazorpayEnabled || string.IsNullOrWhiteSpace(secrets.KeyId) || string.IsNullOrWhiteSpace(secrets.KeySecret))
            throw new InvalidOperationException("Razorpay is not configured.");

        var client = _httpClientFactory.CreateClient("Razorpay");
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secrets.KeyId}:{secrets.KeySecret}"));
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

        var body = JsonSerializer.Serialize(new
        {
            amount = amountPaise,
            currency = "INR",
            receipt = $"rcpt_{Guid.NewGuid():N}",
            notes = new { playerId = playerId.ToString() }
        });

        var response = await client.PostAsync(
            "https://api.razorpay.com/v1/orders",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Razorpay order id missing from response.");
    }
}
