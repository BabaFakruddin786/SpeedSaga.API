using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpeedSaga.API.Services;

public interface IRazorpayService
{
    bool VerifySignature(string orderId, string paymentId, string signature);
    Task<string> CreateOrderAsync(long amountPaise, Guid playerId);
    string GetKeyId();
}

public class RazorpayService : IRazorpayService
{
    private readonly string _keyId;
    private readonly string _secret;
    private readonly IHttpClientFactory _httpClientFactory;

    public RazorpayService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _keyId = configuration["Razorpay:KeyId"] ?? string.Empty;
        _secret = configuration["Razorpay:KeySecret"] ?? string.Empty;
        _httpClientFactory = httpClientFactory;
    }

    public string GetKeyId() => _keyId;

    public bool VerifySignature(string orderId, string paymentId, string signature)
    {
        if (string.IsNullOrWhiteSpace(_secret))
            return false;

        var payload = $"{orderId}|{paymentId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return expected == signature.ToLowerInvariant();
    }

    public async Task<string> CreateOrderAsync(long amountPaise, Guid playerId)
    {
        var client = _httpClientFactory.CreateClient("Razorpay");
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_keyId}:{_secret}"));
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
