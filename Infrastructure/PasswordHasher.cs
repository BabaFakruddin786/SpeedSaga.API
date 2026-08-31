using System.Security.Cryptography;
using System.Text;

namespace SpeedSaga.API.Infrastructure;

public static class PasswordHasher
{
    public static string GenerateSalt()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static string Hash(string password, string salt)
    {
        using var sha = SHA256.Create();
        var combined = Encoding.UTF8.GetBytes(password + salt);
        return Convert.ToBase64String(sha.ComputeHash(combined));
    }

    public static bool Verify(string password, string salt, string expectedHash)
        => string.Equals(Hash(password, salt), expectedHash, StringComparison.Ordinal);
}
