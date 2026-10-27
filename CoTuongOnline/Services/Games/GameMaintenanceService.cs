using CoTuongOnline.Repositories;
using CoTuongOnline.Services.Auth;
using CoTuongOnline.Services.Rooms;

namespace CoTuongOnline.Services.Games;

/// <summary>
/// Bộ hẹn giờ chạy nền (mỗi 0,5 giây):
/// - Phát hiện hết giờ của ván online (server tự xử, không chờ client).
/// - Mất kết nối: 15 giây → PLAYER_OFFLINE, thêm 60 giây → xử thua.
/// - Hết hạn lời mời (30 giây), người rời phòng khi offline quá lâu, giải phóng RAM của ván với máy lâu không chơi
///   (ván vẫn nằm trong DB, mở lại là nạp lại), xoá tài khoản chưa xác nhận OTP quá 24 giờ.
/// </summary>
public sealed class GameMaintenanceService : BackgroundService
{
    private readonly OnlineGameService _games;
    private readonly RoomService _rooms;
    private readonly InviteService _invites;
    private readonly AiGameService _aiGames;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GameMaintenanceService> _logger;

    public GameMaintenanceService(OnlineGameService games, RoomService rooms, InviteService invites,
        AiGameService aiGames, IServiceScopeFactory scopes, ILogger<GameMaintenanceService> logger)
    {
        _games = games;
        _rooms = rooms;
        _invites = invites;
        _aiGames = aiGames;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        long tick = 0;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            tick++;
            var now = DateTime.UtcNow;
            try
            {
                await _games.TickAsync(now);
                await _invites.ExpireAsync(now);
                if (tick % 10 == 0) await _rooms.CleanupIdleMembersAsync(now);
                if (tick % 600 == 0) _aiGames.Cleanup(now);
                if (tick % 7200 == 0) await DeleteStaleUnverifiedAccountsAsync(now); // mỗi giờ
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong vòng hẹn giờ");
            }
        }
    }

    private async Task DeleteStaleUnverifiedAccountsAsync(DateTime now)
    {
        using var scope = _scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        int removed = await users.DeleteUnverifiedCreatedBeforeAsync(now - AuthService.UnverifiedAccountLifetime);
        if (removed > 0) _logger.LogInformation("Đã xoá {Count} tài khoản chưa xác nhận OTP quá 24 giờ", removed);
    }
}
