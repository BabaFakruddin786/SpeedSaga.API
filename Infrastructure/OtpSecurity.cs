using System.Security.Cryptography;
using System.Text;

namespace SpeedSaga.API.Infrastructure;

public static class OtpSecurity
{
    public static string GenerateSalt()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static string HashOtp(string otp, string salt)
    {
        using var sha = SHA256.Create();
        var combined = Encoding.UTF8.GetBytes(otp + salt);
        return Convert.ToBase64String(sha.ComputeHash(combined));
    }

    public static string GenerateOtp(int length)
    {
        var min = (int)Math.Pow(10, length - 1);
        var max = (int)Math.Pow(10, length) - 1;
        return Random.Shared.Next(min, max + 1).ToString();
    }

    public static string MaskPhone(string phone)
    {
        phone = phone.Trim();
        if (phone.Length <= 4) return "****";
        return $"******{phone[^4..]}";
    }

    public static string MaskEmail(string email)
    {
        email = email.Trim();
        var at = email.IndexOf('@');
        if (at <= 1) return "****@****";
        return $"{email[0]}***{email[(at - 1)..]}";
    }

    public static string MaskDestination(string destination, string channel)
        => channel == Models.MessageChannels.Email
            ? MaskEmail(destination)
            : MaskPhone(destination);
}
