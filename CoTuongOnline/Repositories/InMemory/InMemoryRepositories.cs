using System.Collections.Concurrent;
using CoTuongOnline.Models;

namespace CoTuongOnline.Repositories.InMemory;

/// <summary>
/// "Cơ sở dữ liệu" tạm thời trong RAM (đăng ký Singleton). Mỗi ConcurrentDictionary tương ứng một bảng.
/// Dữ liệu mất khi tắt server — đúng với giai đoạn chưa gắn SQL Server.
/// </summary>
public sealed class InMemoryDataStore
{
    public ConcurrentDictionary<int, User> Users { get; } = new();
    public ConcurrentDictionary<int, OtpCode> Otps { get; } = new();
    public ConcurrentDictionary<long, GameRecord> Games { get; } = new();
    public ConcurrentDictionary<long, GameMoveRecord> GameMoves { get; } = new();
    public ConcurrentDictionary<long, SavedGame> SavedGames { get; } = new();

    private int _userId;
    private int _otpId;
    private long _gameId;
    private long _moveId;
    private long _savedId;

    public int NextUserId() => Interlocked.Increment(ref _userId);
    public int NextOtpId() => Interlocked.Increment(ref _otpId);
    public long NextGameId() => Interlocked.Increment(ref _gameId);
    public long NextMoveId() => Interlocked.Increment(ref _moveId);
    public long NextSavedId() => Interlocked.Increment(ref _savedId);
}

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly InMemoryDataStore _db;
    public InMemoryUserRepository(InMemoryDataStore db) => _db = db;

    public Task<User?> GetByIdAsync(int id) => Task.FromResult(_db.Users.GetValueOrDefault(id));

    public Task<User?> GetByEmailAsync(string email) =>
        Task.FromResult(_db.Users.Values.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));

    public Task<User?> GetByNicknameAsync(string nickname) =>
        Task.FromResult(_db.Users.Values.FirstOrDefault(u => string.Equals(u.Nickname, nickname, StringComparison.OrdinalIgnoreCase)));

    public Task<List<User>> SearchByNicknameAsync(string keyword, int excludeUserId, int limit) =>
        Task.FromResult(_db.Users.Values
            .Where(u => u.Id != excludeUserId && u.IsVerified &&
                        u.Nickname.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.Nickname.Length).ThenBy(u => u.Nickname)
            .Take(limit).ToList());

    public Task<List<User>> GetByIdsAsync(IEnumerable<int> ids) =>
        Task.FromResult(ids.Distinct().Select(id => _db.Users.GetValueOrDefault(id)).Where(u => u is not null).Select(u => u!).ToList());

    public Task<User> AddAsync(User user)
    {
        user.Id = _db.NextUserId();
        _db.Users[user.Id] = user;
        return Task.FromResult(user);
    }

    public Task UpdateAsync(User user)
    {
        _db.Users[user.Id] = user;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(User user)
    {
        _db.Users.TryRemove(user.Id, out _);
        return Task.CompletedTask;
    }

    public Task<int> DeleteUnverifiedCreatedBeforeAsync(DateTime cutoff)
    {
        var stale = _db.Users.Values.Where(u => !u.IsVerified && u.CreatedAt < cutoff).ToList();
        foreach (var u in stale) _db.Users.TryRemove(u.Id, out _);
        return Task.FromResult(stale.Count);
    }
}

public sealed class InMemoryOtpRepository : IOtpRepository
{
    private readonly InMemoryDataStore _db;
    public InMemoryOtpRepository(InMemoryDataStore db) => _db = db;

    public Task<OtpCode?> GetLatestAsync(string email, string purpose) =>
        Task.FromResult(_db.Otps.Values
            .Where(o => string.Equals(o.Email, email, StringComparison.OrdinalIgnoreCase) && o.Purpose == purpose)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .FirstOrDefault());

    public Task<OtpCode> AddAsync(OtpCode otp)
    {
        otp.Id = _db.NextOtpId();
        _db.Otps[otp.Id] = otp;
        return Task.FromResult(otp);
    }

    public Task UpdateAsync(OtpCode otp)
    {
        _db.Otps[otp.Id] = otp;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryGameRepository : IGameRepository
{
    private readonly InMemoryDataStore _db;
    public InMemoryGameRepository(InMemoryDataStore db) => _db = db;

    public Task<GameRecord> AddAsync(GameRecord game)
    {
        game.Id = _db.NextGameId();
        _db.Games[game.Id] = game;
        return Task.FromResult(game);
    }

    public Task UpdateAsync(GameRecord game)
    {
        _db.Games[game.Id] = game;
        return Task.CompletedTask;
    }

    public Task<GameRecord?> GetByIdAsync(long id) => Task.FromResult(_db.Games.GetValueOrDefault(id));

    public Task RecordMoveAsync(GameMoveRecord move, string currentStateJson)
    {
        move.Id = _db.NextMoveId();
        _db.GameMoves[move.Id] = move;
        if (_db.Games.TryGetValue(move.GameId, out var game)) game.CurrentStateJson = currentStateJson;
        return Task.CompletedTask;
    }

    public Task<List<GameRecord>> GetRecentByUserAsync(int userId, int limit) =>
        Task.FromResult(_db.Games.Values
            .Where(g => g.RedPlayerId == userId || g.BlackPlayerId == userId)
            .OrderByDescending(g => g.StartedAt).Take(limit).ToList());

    public Task<int> AbortUnfinishedOnlineGamesAsync()
    {
        var stale = _db.Games.Values.Where(g => g.Status == "PLAYING" && g.OpponentType == "HUMAN").ToList();
        foreach (var g in stale)
        {
            g.Status = "ABORTED";
            g.EndReason = "SERVER_RESTART";
            g.EndedAt = DateTime.UtcNow;
        }
        return Task.FromResult(stale.Count);
    }
}

public sealed class InMemorySavedGameRepository : ISavedGameRepository
{
    private readonly InMemoryDataStore _db;
    public InMemorySavedGameRepository(InMemoryDataStore db) => _db = db;

    public Task<List<SavedGame>> ListByUserAsync(int userId) =>
        Task.FromResult(_db.SavedGames.Values.Where(s => s.UserId == userId).OrderByDescending(s => s.UpdatedAt).ToList());

    public Task<SavedGame?> GetByIdAsync(long id) => Task.FromResult(_db.SavedGames.GetValueOrDefault(id));

    public Task<int> CountByUserAsync(int userId) => Task.FromResult(_db.SavedGames.Values.Count(s => s.UserId == userId));

    public Task<SavedGame> AddAsync(SavedGame saved)
    {
        saved.Id = _db.NextSavedId();
        _db.SavedGames[saved.Id] = saved;
        return Task.FromResult(saved);
    }

    public Task UpdateAsync(SavedGame saved)
    {
        _db.SavedGames[saved.Id] = saved;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(long id)
    {
        _db.SavedGames.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
