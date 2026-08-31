namespace SpeedSaga.API.Models;

public class AdminOptions
{
    public const string SectionName = "Admin";
    public string ApiKey { get; set; } = "";
    public AdminSeedUser[] SeedUsers { get; set; } = Array.Empty<AdminSeedUser>();
}

public class AdminSeedUser
{
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string DisplayName { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "Support";
}

public record AdminLoginRequest(string Contact, string Password);
