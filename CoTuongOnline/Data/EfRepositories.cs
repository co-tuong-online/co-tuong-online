using CoTuongOnline.Models;
using CoTuongOnline.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CoTuongOnline.Data;

// Cài đặt repository bằng EF Core + SQL Server. Đăng ký Scoped (1 DbContext / request hoặc / scope).

public sealed class EfUserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public EfUserRepository(AppDbContext db) => _db = db;

    public async Task<User?> GetByIdAsync(int id) => await _db.Users.FindAsync(id);

    // Collation mặc định của SQL Server không phân biệt hoa/thường nên so sánh "==" là đủ
    public Task<User?> GetByEmailAsync(string email) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email);

    public Task<User?> GetByNicknameAsync(string nickname) =>
        _db.Users.FirstOrDefaultAsync(u => u.Nickname == nickname);

    public Task<List<User>> SearchByNicknameAsync(string keyword, int excludeUserId, int limit) =>
        _db.Users
            .Where(u => u.Id != excludeUserId && u.IsVerified && u.Nickname.Contains(keyword))
            .OrderBy(u => u.Nickname.Length).ThenBy(u => u.Nickname)
            .Take(limit)
            .ToListAsync();

    public Task<List<User>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return _db.Users.Where(u => list.Contains(u.Id)).ToListAsync();
    }

    public async Task<User> AddAsync(User user)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task UpdateAsync(User user)
    {
        if (_db.Entry(user).State == EntityState.Detached) _db.Users.Update(user);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(User user)
    {
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
    }

    public Task<int> DeleteUnverifiedCreatedBeforeAsync(DateTime cutoff) =>
        _db.Users.Where(u => !u.IsVerified && u.CreatedAt < cutoff).ExecuteDeleteAsync();
}

public sealed class EfOtpRepository : IOtpRepository
{
    private readonly AppDbContext _db;
    public EfOtpRepository(AppDbContext db) => _db = db;

    public Task<OtpCode?> GetLatestAsync(string email, string purpose) =>
        _db.OtpCodes
            .Where(o => o.Email == email && o.Purpose == purpose)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync();

    public async Task<OtpCode> AddAsync(OtpCode otp)
    {
        _db.OtpCodes.Add(otp);
        await _db.SaveChangesAsync();
        return otp;
    }

    public async Task UpdateAsync(OtpCode otp)
    {
        if (_db.Entry(otp).State == EntityState.Detached) _db.OtpCodes.Update(otp);
        await _db.SaveChangesAsync();
    }
}

public sealed class EfGameRepository : IGameRepository
{
    private readonly AppDbContext _db;
    public EfGameRepository(AppDbContext db) => _db = db;

    public async Task<GameRecord> AddAsync(GameRecord game)
    {
        _db.Games.Add(game);
        await _db.SaveChangesAsync();
        return game;
    }

    public async Task UpdateAsync(GameRecord game)
    {
        if (_db.Entry(game).State == EntityState.Detached) _db.Games.Update(game);
        await _db.SaveChangesAsync();
    }

    public async Task<GameRecord?> GetByIdAsync(long id) => await _db.Games.FindAsync(id);

    public async Task RecordMoveAsync(GameMoveRecord move, string currentStateJson)
    {
        // Ván với máy có "đi lại": các nước từ ply này trở đi (nếu còn) là nước đã bị huỷ → xoá
        var stale = await _db.GameMoves.Where(m => m.GameId == move.GameId && m.Ply >= move.Ply).ToListAsync();
        _db.GameMoves.RemoveRange(stale);
        _db.GameMoves.Add(move);

        var game = await _db.Games.FindAsync(move.GameId);
        if (game is not null) game.CurrentStateJson = currentStateJson;

        await _db.SaveChangesAsync(); // 1 lần lưu = 1 transaction
    }

    public Task<List<GameRecord>> GetRecentByUserAsync(int userId, int limit) =>
        _db.Games
            .Where(g => g.RedPlayerId == userId || g.BlackPlayerId == userId)
            .OrderByDescending(g => g.StartedAt)
            .Take(limit)
            .ToListAsync();

    public Task<int> AbortUnfinishedOnlineGamesAsync()
    {
        var now = DateTime.UtcNow;
        return _db.Games
            .Where(g => g.Status == "PLAYING" && g.OpponentType == "HUMAN")
            .ExecuteUpdateAsync(set => set
                .SetProperty(g => g.Status, "ABORTED")
                .SetProperty(g => g.EndReason, "SERVER_RESTART")
                .SetProperty(g => g.EndedAt, now));
    }
}

public sealed class EfSavedGameRepository : ISavedGameRepository
{
    private readonly AppDbContext _db;
    public EfSavedGameRepository(AppDbContext db) => _db = db;

    public Task<List<SavedGame>> ListByUserAsync(int userId) =>
        _db.SavedGames.Where(s => s.UserId == userId).OrderByDescending(s => s.UpdatedAt).ToListAsync();

    public async Task<SavedGame?> GetByIdAsync(long id) => await _db.SavedGames.FindAsync(id);

    public Task<int> CountByUserAsync(int userId) => _db.SavedGames.CountAsync(s => s.UserId == userId);

    public async Task<SavedGame> AddAsync(SavedGame saved)
    {
        _db.SavedGames.Add(saved);
        await _db.SaveChangesAsync();
        return saved;
    }

    public async Task UpdateAsync(SavedGame saved)
    {
        if (_db.Entry(saved).State == EntityState.Detached) _db.SavedGames.Update(saved);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(long id)
    {
        var saved = await _db.SavedGames.FindAsync(id);
        if (saved is null) return;
        _db.SavedGames.Remove(saved);
        await _db.SaveChangesAsync();
    }
}
