using System.Security.Claims;
using System.Text;
using FluentValidation;
using FluentValidation.AspNetCore;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using SpeedSaga.API.Authorization;
using SpeedSaga.API.Hubs;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Middleware;
using SpeedSaga.API.Models;
using SpeedSaga.API.Services;
using SpeedSaga.API.Validators;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings are not configured.");

builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddSingleton<SessionMoveStore>();
builder.Services.AddSingleton<TicTacToeStateStore>();
builder.Services.AddSingleton<MovePersistenceService>();
builder.Services.AddSingleton<IMovePersistenceQueue>(sp => sp.GetRequiredService<MovePersistenceService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MovePersistenceService>());
builder.Services.AddHostedService<SessionMoveStoreCleanup>();
builder.Services.AddHttpClient("Razorpay");
builder.Services.AddHttpClient("Msg91");
builder.Services.AddHttpClient("Fcm");

builder.Services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.SectionName));
builder.Services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<LogViewerOptions>(configuration.GetSection(LogViewerOptions.SectionName));
builder.Services.AddSingleton<GameConnectionTracker>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IGamePlayConfigService, GamePlayConfigService>();
builder.Services.AddScoped<IPromoService, PromoService>();
builder.Services.AddSingleton<MessageDispatchService>();
builder.Services.AddSingleton<IMessageDispatchQueue>(sp => sp.GetRequiredService<MessageDispatchService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MessageDispatchService>());
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IMessageDeliveryService, MessageDeliveryService>();
builder.Services.AddScoped<IOutgoingMessageService, OutgoingMessageService>();
builder.Services.AddSingleton<KycDocumentStorage>();
builder.Services.AddScoped<IKycVerificationService, KycVerificationService>();
builder.Services.AddScoped<IKycAdminService, KycAdminService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminFinanceService, AdminFinanceService>();
builder.Services.AddScoped<IAdminPlayerService, AdminPlayerService>();
builder.Services.AddScoped<IAdminLevelService, AdminLevelService>();
builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
builder.Services.AddScoped<IPaymentConfigService, PaymentConfigService>();
builder.Services.AddScoped<IAdminThemeService, AdminThemeService>();
builder.Services.AddScoped<IAdminNotificationService, AdminNotificationService>();
builder.Services.AddScoped<IFcmPushService, FcmPushService>();
builder.Services.AddHostedService<AdminUserSeedService>();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 6_000_000);
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<ILevelService, LevelService>();
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddScoped<IPlayerService, PlayerService>();
builder.Services.AddScoped<IRazorpayService, RazorpayService>();
builder.Services.AddScoped<IBotDetectionService, BotDetectionService>();
builder.Services.AddScoped<ITournamentService, TournamentService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ISupportService, SupportService>();
builder.Services.AddScoped<ISupportConfigService, SupportConfigService>();
builder.Services.AddScoped<ITickerConfigService, TickerConfigService>();
builder.Services.AddScoped<IThemeService, ThemeService>();
builder.Services.AddSingleton<ILogViewerService, LogViewerService>();
builder.Services.AddHostedService<PuzzleWarmupService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.PlayerOnly, policy =>
        policy.RequireAuthenticatedUser().RequireRole(AppRoles.Player));
});

builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

var connectionString = configuration.GetConnectionString("SpeedSagaDB");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddHangfire(config => config.UseSqlServerStorage(connectionString));
    builder.Services.AddHangfireServer();
}

var app = builder.Build();

var logViewerOptions = app.Configuration.GetSection(LogViewerOptions.SectionName).Get<LogViewerOptions>();
var apiLogDir = logViewerOptions?.ApiLogDirectory;
if (!string.IsNullOrWhiteSpace(apiLogDir) && !Path.IsPathRooted(apiLogDir))
    apiLogDir = Path.Combine(app.Environment.ContentRootPath, apiLogDir);
AppFileLogger.Initialize(apiLogDir);
AppFileLogger.Info(AppFileLogger.Category.App, "API_START", $"env={app.Environment.EnvironmentName}");

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

if (!string.IsNullOrWhiteSpace(connectionString))
{
    app.UseHangfireDashboard("/hangfire");

    using var scope = app.Services.CreateScope();
    var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobs.AddOrUpdate<IGameService>("recalc-winrates", s => s.RecalculateWinRates(), "*/30 * * * *");
    recurringJobs.AddOrUpdate<IGameService>("cleanup-queue", s => s.CleanupExpiredQueue(), "*/1 * * * *");
    recurringJobs.AddOrUpdate<IBotDetectionService>("bot-scan", s => s.RunBotScan(), "0 * * * *");
    recurringJobs.AddOrUpdate<IGameService>("cleanup-stale-2p", s => s.CleanupStaleTwoPlayerSessionsAsync(), "*/5 * * * *");
    recurringJobs.AddOrUpdate<IOtpService>("cleanup-otp-sessions", s => s.CleanupExpiredSessionsAsync(), "0 3 * * *");
}

app.Run();
