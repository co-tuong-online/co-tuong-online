using System.Collections.Concurrent;
using System.Security.Cryptography;
using CoTuongOnline.Dtos;
using CoTuongOnline.Services.Realtime;

namespace CoTuongOnline.Services.Rooms;

public static class RoomStatus
{
    public const string Waiting = "WAITING";          // mới có chủ phòng
    public const string ReadyCheck = "READY_CHECK";   // đủ 2 người, chờ sẵn sàng / bắt đầu
    public const string Playing = "PLAYING";          // đang có ván
}

/// <summary>
/// Phòng chơi đang hoạt động. Phòng là trạng thái "sống" nên luôn nằm trong RAM,
/// kể cả khi đã có DB (DB chỉ lưu lịch sử ván). Mọi thay đổi phải lock(room.Sync).
/// </summary>
public sealed class Room
{
    public const int MaxChatHistory = 100;

    public required string Code { get; init; }
    public int OwnerId { get; set; }
    public string OwnerNickname { get; set; } = "";
    public int? GuestId { get; set; }
    public string? GuestNickname { get; set; }
    public bool GuestReady { get; set; }
    public string Status { get; set; } = RoomStatus.Waiting;
    public RoomConfigDto Config { get; set; } = new();
    public long? CurrentGameId { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public List<ChatMessageDto> Messages { get; } = new();
    public long NextMessageId;
    public Dictionary<int, DateTime> LastChatAt { get; } = new();

    public object Sync { get; } = new();

    public bool IsMember(int userId) => OwnerId == userId || GuestId == userId;
    public int? OpponentOf(int userId) => userId == OwnerId ? GuestId : userId == GuestId ? OwnerId : null;
    public IEnumerable<int> Members => GuestId is int g ? new[] { OwnerId, g } : new[] { OwnerId };
}

/// <summary>Kho phòng đang mở + chỉ mục "user đang ở phòng nào" (mỗi user tối đa 1 phòng).</summary>
public sealed class RoomRegistry
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly ConcurrentDictionary<int, string> _userRoom = new();

    public Room? Get(string code) => _rooms.GetValueOrDefault(code);

    public Room? FindByUser(int userId) =>
        _userRoom.TryGetValue(userId, out var code) ? Get(code) : null;

    public string? RoomCodeOf(int userId) => _userRoom.GetValueOrDefault(userId);

    public IEnumerable<Room> All => _rooms.Values;

    /// <summary>Sinh mã phòng 6 chữ số (100000–999999) chưa trùng.</summary>
    public Room CreateRoom(Func<string, Room> factory)
    {
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            string code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
            var room = factory(code);
            if (_rooms.TryAdd(code, room)) return room;
        }
        throw new InvalidOperationException("Không sinh được mã phòng.");
    }

    public void Remove(string code) => _rooms.TryRemove(code, out _);

    public void SetUserRoom(int userId, string code) => _userRoom[userId] = code;

    public void ClearUserRoom(int userId, string code) =>
        _userRoom.TryRemove(new KeyValuePair<int, string>(userId, code));
}

public static class RoomMapper
{
    public static RoomDto ToDto(Room r, PresenceTracker presence) => new(
        r.Code,
        r.Status,
        r.OwnerId,
        new PlayerDto(r.OwnerId, r.OwnerNickname, presence.IsOnline(r.OwnerId)),
        r.GuestId is int g ? new PlayerDto(g, r.GuestNickname ?? "?", presence.IsOnline(g)) : null,
        r.GuestReady,
        r.Config.Clone(),
        r.CurrentGameId,
        r.CreatedAt);
}
