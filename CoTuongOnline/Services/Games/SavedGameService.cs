using System.Text.Json;
using CoTuongOnline.Common;
using CoTuongOnline.Dtos;
using CoTuongOnline.Engine;
using CoTuongOnline.Models;
using CoTuongOnline.Repositories;
using CoTuongOnline.Services.Realtime;
using CoTuongOnline.Services.Rooms;

namespace CoTuongOnline.Services.Games;

/// <summary>Lưu / tải ván với máy (phạm vi rút gọn: chỉ ván với máy).</summary>
public sealed class SavedGameService
{
    public const int MaxSavesPerUser = 20;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ISavedGameRepository _repo;
    private readonly AiGameService _aiGames;

    public SavedGameService(ISavedGameRepository repo, AiGameService aiGames)
    {
        _repo = repo;
        _aiGames = aiGames;
    }

    public async Task<List<SavedGameDto>> ListAsync(int userId) =>
        (await _repo.ListByUserAsync(userId)).Select(ToDto).ToList();

    public async Task<SavedGameDto> SaveAsync(int userId, SaveGameRequest req)
    {
        var (game, state) = await _aiGames.GetForSaveAsync(userId, req.GameId);
        string name = req.Name.Trim();
        string stateJson = JsonSerializer.Serialize(state, Json);

        SavedGame saved;
        if (req.OverwriteId is long overwriteId)
        {
            saved = await RequireOwnAsync(userId, overwriteId);
            saved.Name = name;
            saved.StateJson = stateJson;
            saved.MoveCount = state.Moves.Count;
            saved.AiLevel = game.Level;
            saved.PlayerColor = Pieces.ColorName(game.PlayerColor);
            saved.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(saved);
        }
        else
        {
            if (await _repo.CountByUserAsync(userId) >= MaxSavesPerUser)
                throw new AppException(ErrorCodes.SaveLimit, $"Bạn đã có {MaxSavesPerUser} ván lưu. Hãy xoá bớt hoặc ghi đè một bản cũ.");
            saved = await _repo.AddAsync(new SavedGame
            {
                UserId = userId,
                Name = name,
                OpponentType = "AI",
                AiLevel = game.Level,
                PlayerColor = Pieces.ColorName(game.PlayerColor),
                StateJson = stateJson,
                MoveCount = state.Moves.Count
            });
        }
        _aiGames.MarkSaved(game.Id, saved.Id, saved.Name);
        return ToDto(saved);
    }

    public async Task<SavedGameDto> RenameAsync(int userId, long id, string name)
    {
        var saved = await RequireOwnAsync(userId, id);
        saved.Name = name.Trim();
        saved.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(saved);
        return ToDto(saved);
    }

    public async Task DeleteAsync(int userId, long id)
    {
        await RequireOwnAsync(userId, id);
        await _repo.DeleteAsync(id);
        _aiGames.ForgetSave(id);
    }

    /// <summary>Tiếp tục ván đã lưu: tạo ván với máy mới từ trạng thái đã lưu (cùng độ khó và màu quân).</summary>
    public async Task<AiGameDto> ResumeAsync(int userId, long id)
    {
        var saved = await RequireOwnAsync(userId, id);
        BoardState state;
        try
        {
            state = JsonSerializer.Deserialize<BoardState>(saved.StateJson, Json)
                    ?? throw new FormatException("trống");
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new AppException(ErrorCodes.ValidationError, "Bản lưu bị hỏng, không thể tải.");
        }
        return await _aiGames.CreateAsync(userId, saved.AiLevel, saved.PlayerColor, state, saved.Id, saved.Name);
    }

    private async Task<SavedGame> RequireOwnAsync(int userId, long id)
    {
        var saved = await _repo.GetByIdAsync(id);
        if (saved is null || saved.UserId != userId)
            throw AppException.NotFound(ErrorCodes.SaveNotFound, "Không tìm thấy ván đã lưu.");
        return saved;
    }

    private static SavedGameDto ToDto(SavedGame s)
    {
        var state = JsonSerializer.Deserialize<BoardState>(s.StateJson, Json);
        return new SavedGameDto(s.Id, s.Name, s.AiLevel, s.PlayerColor, s.MoveCount,
            state?.SideToMove ?? "red", state?.Board ?? Array.Empty<string?[]>(), s.CreatedAt, s.UpdatedAt);
    }
}

/// <summary>Tìm người chơi để mời + danh sách đang online.</summary>
public sealed class UserService
{
    private readonly IUserRepository _users;
    private readonly PresenceTracker _presence;
    private readonly RoomRegistry _rooms;

    public UserService(IUserRepository users, PresenceTracker presence, RoomRegistry rooms)
    {
        _users = users;
        _presence = presence;
        _rooms = rooms;
    }

    public async Task<List<UserSearchItem>> SearchAsync(int userId, string? keyword)
    {
        keyword = (keyword ?? "").Trim();
        if (keyword.Length < 1) return new List<UserSearchItem>();
        var found = await _users.SearchByNicknameAsync(keyword, userId, 10);
        return found
            .Select(u => new UserSearchItem(u.Id, u.Nickname, _presence.IsOnline(u.Id), _rooms.RoomCodeOf(u.Id)))
            .OrderByDescending(u => u.Online)
            .ToList();
    }

    public List<UserSearchItem> Online(int userId) =>
        _presence.OnlineUsers()
            .Where(u => u.UserId != userId)
            .OrderBy(u => u.Nickname)
            .Take(50)
            .Select(u => new UserSearchItem(u.UserId, u.Nickname, true, _rooms.RoomCodeOf(u.UserId)))
            .ToList();

    public int OnlineCount => _presence.OnlineCount;
}
