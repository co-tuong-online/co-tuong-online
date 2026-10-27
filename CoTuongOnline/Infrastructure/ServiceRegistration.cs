using CoTuongOnline.Data;
using CoTuongOnline.Models;
using CoTuongOnline.Repositories;
using CoTuongOnline.Services.Auth;
using CoTuongOnline.Services.Email;
using CoTuongOnline.Services.Games;
using CoTuongOnline.Services.Realtime;
using CoTuongOnline.Services.Rooms;
using Microsoft.Extensions.Options;

namespace CoTuongOnline.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<EmailOptions>(config.GetSection(EmailOptions.Section));
        services.Configure<SeedOptions>(config.GetSection(SeedOptions.Section));

        // ---------------- Tầng dữ liệu ----------------
        // SQL Server (mặc định) hoặc RAM, chọn bằng "Database:Provider" — xem Data/DatabaseSetup.cs
        services.AddAppDatabase(config);

        // ---------------- Xác thực / email ----------------
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<LoginAttemptTracker>();
        services.AddScoped<AuthService>();
        bool devMail = config.GetSection(EmailOptions.Section).Get<EmailOptions>()?.DevMode ?? true;
        if (devMail) services.AddSingleton<IEmailSender, DevEmailSender>();
        else services.AddSingleton<IEmailSender, SmtpEmailSender>();

        // ---------------- Trạng thái sống (luôn ở RAM, kể cả khi có DB) ----------------
        services.AddSingleton<PresenceTracker>();
        services.AddSingleton<RealtimeNotifier>();
        services.AddSingleton<RoomRegistry>();
        services.AddSingleton<OnlineGameService>();
        services.AddSingleton<RoomService>();
        services.AddSingleton<InviteService>();
        services.AddSingleton<AiGameService>();
        services.AddHostedService<GameMaintenanceService>();

        // ---------------- Service theo request ----------------
        services.AddScoped<SavedGameService>();
        services.AddScoped<UserService>();

        return services;
    }

    /// <summary>Tạo tài khoản demo nếu chưa có (chạy mỗi lần khởi động, đã có thì bỏ qua).</summary>
    public static async Task SeedDemoDataAsync(this WebApplication app)
    {
        var seed = app.Services.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (!seed.DemoUsers) return;

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var demo = new[]
        {
            ("khoi@demo.vn", "khoi_pham"),
            ("minh@demo.vn", "minh_ng"),
            ("dang@demo.vn", "dang_hd"),
            ("quan@demo.vn", "quan_td"),
        };
        int created = 0;
        foreach (var (email, nick) in demo)
        {
            if (await users.GetByEmailAsync(email) is not null) continue;
            await users.AddAsync(new User
            {
                Email = email,
                Nickname = nick,
                PasswordHash = hasher.Hash("demo1234"),
                IsVerified = true
            });
            created++;
        }
        if (created > 0)
            app.Logger.LogInformation("Đã tạo {Count} tài khoản demo (mật khẩu: demo1234)", created);
    }

    /// <summary>
    /// Dọn dữ liệu dở dang sau khi server khởi động lại:
    /// ván online đang chơi dở → ABORTED (phòng và đồng hồ đã mất cùng RAM),
    /// tài khoản chưa xác nhận OTP quá 24 giờ → xoá.
    /// Ván với máy KHÔNG bị đụng tới: người chơi mở lại link là đánh tiếp được.
    /// </summary>
    public static async Task RecoverAfterRestartAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var games = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        int aborted = await games.AbortUnfinishedOnlineGamesAsync();
        if (aborted > 0)
            app.Logger.LogWarning("Đã đánh dấu {Count} ván online bị gián đoạn do server khởi động lại (ABORTED)", aborted);

        int removed = await users.DeleteUnverifiedCreatedBeforeAsync(DateTime.UtcNow - AuthService.UnverifiedAccountLifetime);
        if (removed > 0)
            app.Logger.LogInformation("Đã xoá {Count} tài khoản chưa xác nhận OTP quá 24 giờ", removed);
    }
}
