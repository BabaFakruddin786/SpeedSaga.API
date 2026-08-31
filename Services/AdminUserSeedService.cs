using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SpeedSaga.API.Authorization;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public class AdminUserSeedService : IHostedService
{
    readonly IServiceProvider _services;
    readonly ILogger<AdminUserSeedService> _log;

    public AdminUserSeedService(IServiceProvider services, ILogger<AdminUserSeedService> log)
    {
        _services = services;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlConnectionFactory>();
        var admin = scope.ServiceProvider.GetRequiredService<IOptions<AdminOptions>>().Value;

        await using var cn = db.CreateConnection();
        await cn.OpenAsync(cancellationToken);
        await using var countCmd = new SqlCommand("USP_AdminCountUsers", cn) { CommandType = CommandType.StoredProcedure };
        var count = (int)(await countCmd.ExecuteScalarAsync(cancellationToken) ?? 0);
        if (count > 0) return;

        var seeds = admin.SeedUsers;
        if (seeds.Length == 0)
        {
            seeds =
            [
                new AdminSeedUser
                {
                    Email = "admin@speedsaga.com",
                    Phone = "9999999999",
                    DisplayName = "Super Admin",
                    Password = "Admin@123456",
                    Role = AppRoles.SuperAdmin
                },
                new AdminSeedUser
                {
                    Email = "support@speedsaga.com",
                    Phone = "8888888888",
                    DisplayName = "Support Agent",
                    Password = "Support@123456",
                    Role = AppRoles.Support
                }
            ];
        }

        foreach (var seed in seeds)
        {
            if (string.IsNullOrWhiteSpace(seed.Password) || string.IsNullOrWhiteSpace(seed.DisplayName))
                continue;
            if (string.IsNullOrWhiteSpace(seed.Email) && string.IsNullOrWhiteSpace(seed.Phone))
                continue;

            var role = seed.Role is AppRoles.SuperAdmin or AppRoles.Support ? seed.Role : AppRoles.Support;
            var salt = PasswordHasher.GenerateSalt();
            var hash = PasswordHasher.Hash(seed.Password, salt);

            await using var cmd = new SqlCommand("USP_AdminCreateUser", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Email", (object?)seed.Email?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Phone", (object?)seed.Phone?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DisplayName", seed.DisplayName.Trim());
            cmd.Parameters.AddWithValue("@PasswordHash", hash);
            cmd.Parameters.AddWithValue("@PasswordSalt", salt);
            cmd.Parameters.AddWithValue("@Role", role);
            var outId = cmd.Parameters.Add("@AdminUserId", SqlDbType.UniqueIdentifier) ;
            outId.Direction = ParameterDirection.Output;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _log.LogInformation("Seeded admin user {DisplayName} ({Role})", seed.DisplayName, role);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
