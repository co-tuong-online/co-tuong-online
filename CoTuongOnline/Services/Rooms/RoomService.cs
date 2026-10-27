using System.Security.Cryptography;
using CoTuongOnline.Common;
using CoTuongOnline.Dtos;
using CoTuongOnline.Services.Games;
using CoTuongOnline.Services.Realtime;

namespace CoTuongOnline.Services.Rooms;

/// <summary>Nghiệp vụ phòng chơi. Singleton vì phòng là trạng thái sống trong RAM.</summary>
public sealed class RoomService
{
    public static readonly TimeSpan IdleLeaveAfter = TimeSpan.FromSeconds(60);
    private static readonly int[] AllowedTotalMinutes = { 3, 5, 10, 15, 20, 30 };

    private readonly RoomRegistry _registry;
    private readonly PresenceTracker _presence;
    private readonly RealtimeNotifier _notifier;
    private readonly OnlineGameService _games;
    private readonly ILogger<RoomService> _logger;

    public RoomService(RoomRegistry registry, PresenceTracker presence, RealtimeNotifier notifier,
        OnlineGameService games, ILogger<RoomService> logger)
    {
        _registry = registry;
        _presence = presence;
        _notifier = notifier;
        _games = games;
        _logger = logger;
    }

    // ------------------------------------------------------------------ truy vấn

    public RoomDto Get(string code)
    {
        var room = Require(code);
        lock (room.Sync) return RoomMapper.ToDto(room, _presence);
    }

    public List<RoomListItem> ListOpen() =>
        _registry.All
            .Where(r => r.Status == RoomStatus.Waiting && !r.Config.IsPrivate)
            .OrderByDescending(r => r.CreatedAt)
            .Take(30)
            .Select(r => new RoomListItem(r.Code, r.OwnerNickname, r.Config.Clone(), r.CreatedAt))
            .ToList();

    public string? CurrentRoomOf(int userId) => _registry.RoomCodeOf(userId);

    // ------------------------------------------------------------------ tạo / vào / rời

    public async Task<RoomDto> CreateAsync(int userId, string nickname, RoomConfigDto? config)
    {
        var cfg = Normalize(config ?? new RoomConfigDto());
        await LeaveCurrentRoomAsync(userId, exceptCode: null);

        var room = _registry.CreateRoom(code => new Room
        {
            Code = code,
            OwnerId = userId,
            OwnerNickname = nickname,
            Config = cfg
        });
        _registry.SetUserRoom(userId, room.Code);
        _logger.LogInformation("{Nick} tạo phòng {Code}", nickname, room.Code);
        lock (room.Sync) return RoomMapper.ToDto(room, _presence);
    }

    public async Task<RoomDto> JoinAsync(int userId, string nickname, string code)
    {
        var room = Require(code);
        bool alreadyMember;
        lock (room.Sync) alreadyMember = room.IsMember(userId);
        if (!alreadyMember) await LeaveCurrentRoomAsync(userId, exceptCode: code);

        RoomDto dto;
        bool newlyJoined = false;
        lock (room.Sync)
        {
            if (_registry.Get(code) is null)
                throw AppException.NotFound(ErrorCodes.RoomNotFound, "Phòng không tồn tại hoặc đã đóng.");
            if (!room.IsMember(userId))
            {
                if (room.Status == RoomStatus.Playing)
                    throw AppException.Conflict(ErrorCodes.RoomPlaying, "Phòng đang có ván, không thể vào.");
                if (room.GuestId is not null)
                    throw AppException.Conflict(ErrorCodes.RoomFull, "Phòng đã đủ 2 người.");
                room.GuestId = userId;
                room.GuestNickname = nickname;
                room.GuestReady = false;
                room.Status = RoomStatus.ReadyCheck;
                _registry.SetUserRoom(userId, code);
                newlyJoined = true;
            }
            dto = RoomMapper.ToDto(room, _presence);
        }

        if (newlyJoined)
        {
            await _notifier.ToRoomAsync(code, Events.PlayerJoined, new
            {
                code,
                player = new { userId, nickname },
                status = dto.Status
            });
            await _notifier.ToRoomAsync(code, Events.RoomUpdated, dto);
        }
        return dto;
    }

    public async Task LeaveAsync(int userId, string code)
    {
        var room = _registry.Get(code);
        if (room is null) return;

        // Đang chơi mà rời phòng → xử thua (như đầu hàng)
        long? gameId;
        lock (room.Sync)
        {
            if (!room.IsMember(userId)) return;
            gameId = room.Status == RoomStatus.Playing ? room.CurrentGameId : null;
        }
        if (gameId is long gid && _games.Get(gid) is { IsPlaying: true } game && game.ColorOf(userId) != 0)
            await _games.ResignAsync(userId, gid);

        RoomDto? dto = null;
        int? newOwner = null;
        bool closed = false;
        lock (room.Sync)
        {
            if (!room.IsMember(userId)) return;
            if (room.GuestId == userId)
            {
                room.GuestId = null;
                room.GuestNickname = null;
            }
            else if (room.GuestId is int guest)
            {
                // Chủ phòng rời đi → chuyển quyền chủ phòng cho khách
                room.OwnerId = guest;
                room.OwnerNickname = room.GuestNickname ?? "?";
                room.GuestId = null;
                room.GuestNickname = null;
                newOwner = guest;
            }
            else
            {
                closed = true;
            }
            room.GuestReady = false;
            if (room.Status != RoomStatus.Playing) room.Status = RoomStatus.Waiting;
            _registry.ClearUserRoom(userId, code);
            if (closed) _registry.Remove(code);
            else dto = RoomMapper.ToDto(room, _presence);
        }

        if (closed)
        {
            _logger.LogInformation("Phòng {Code} đã đóng (không còn ai)", code);
            return;
        }
        await _notifier.ToRoomAsync(code, Events.PlayerLeft, new
        {
            code,
            userId,
            newOwnerId = newOwner ?? dto!.OwnerId,
            status = dto!.Status
        });
        await _notifier.ToRoomAsync(code, Events.RoomUpdated, dto);
    }

    public async Task KickAsync(int ownerId, string code)
    {
        var room = Require(code);
        int guestId;
        lock (room.Sync)
        {
            if (room.OwnerId != ownerId) throw AppException.Forbidden(ErrorCodes.RoomNotOwner, "Chỉ chủ phòng mới được mời người chơi ra.");
            if (room.Status == RoomStatus.Playing) throw AppException.Conflict(ErrorCodes.RoomPlaying, "Không thể mời ra khi đang chơi.");
            if (room.GuestId is not int g) throw new AppException(ErrorCodes.RoomNoGuest, "Phòng chưa có người chơi thứ hai.");
            guestId = g;
        }
        await _notifier.ToUserAsync(guestId, Events.Kicked, new { code });
        await LeaveAsync(guestId, code);
    }

    private async Task LeaveCurrentRoomAsync(int userId, string? exceptCode)
    {
        var current = _registry.FindByUser(userId);
        if (current is null || current.Code == exceptCode) return;
        bool playing;
        lock (current.Sync) playing = current.Status == RoomStatus.Playing && current.IsMember(userId);
        if (playing)
            throw AppException.Conflict(ErrorCodes.AlreadyInGame,
                $"Bạn đang có ván chưa kết thúc ở phòng {current.Code}. Hãy quay lại phòng đó hoặc đầu hàng trước.");
        await LeaveAsync(userId, current.Code);
    }

    // ------------------------------------------------------------------ cấu hình / sẵn sàng / bắt đầu

    public async Task<RoomDto> UpdateConfigAsync(int userId, string code, RoomConfigDto config)
    {
        var room = Require(code);
        RoomDto dto;
        lock (room.Sync)
        {
            if (room.OwnerId != userId) throw AppException.Forbidden(ErrorCodes.RoomNotOwner, "Chỉ chủ phòng mới được đổi cấu hình.");
            if (room.Status == RoomStatus.Playing) throw AppException.Conflict(ErrorCodes.RoomPlaying, "Không thể đổi cấu hình khi đang chơi.");
            room.Config = Normalize(config);
            room.GuestReady = false; // cấu hình đổi → khách phải xác nhận sẵn sàng lại
            dto = RoomMapper.ToDto(room, _presence);
        }
        await _notifier.ToRoomAsync(code, Events.RoomUpdated, dto);
        return dto;
    }

    public async Task<RoomDto> SetReadyAsync(int userId, string code, bool ready)
    {
        var room = Require(code);
        RoomDto dto;
        lock (room.Sync)
        {
            if (room.GuestId != userId) throw AppException.Forbidden(ErrorCodes.RoomNotMember, "Chỉ người chơi thứ hai mới bấm Sẵn sàng.");
            if (room.Status == RoomStatus.Playing) throw AppException.Conflict(ErrorCodes.RoomPlaying, "Ván đang diễn ra.");
            room.GuestReady = ready;
            dto = RoomMapper.ToDto(room, _presence);
        }
        await _notifier.ToRoomAsync(code, Events.RoomUpdated, dto);
        return dto;
    }

    public async Task<GameStateDto> StartAsync(int userId, string code)
    {
        var room = Require(code);
        int ownerId, guestId;
        string ownerNick, guestNick, ownerColor;
        lock (room.Sync)
        {
            if (room.OwnerId != userId) throw AppException.Forbidden(ErrorCodes.RoomNotOwner, "Chỉ chủ phòng mới được bắt đầu ván.");
            if (room.Status == RoomStatus.Playing) throw AppException.Conflict(ErrorCodes.RoomPlaying, "Ván đang diễn ra.");
            if (room.GuestId is not int g) throw new AppException(ErrorCodes.RoomNoGuest, "Cần đủ 2 người để bắt đầu.");
            if (!room.GuestReady) throw new AppException(ErrorCodes.RoomGuestNotReady, "Người chơi thứ hai chưa sẵn sàng.");
            // Giữ chỗ: chuyển sang PLAYING ngay để không ai bấm bắt đầu 2 lần
            room.Status = RoomStatus.Playing;
            ownerId = room.OwnerId;
            ownerNick = room.OwnerNickname;
            guestId = g;
            guestNick = room.GuestNickname ?? "?";
            ownerColor = room.Config.OwnerColor;
        }

        bool ownerRed = ownerColor switch
        {
            "RED" => true,
            "BLACK" => false,
            _ => RandomNumberGenerator.GetInt32(2) == 0
        };

        OnlineGame game;
        try
        {
            game = ownerRed
                ? await _games.CreateGameAsync(room, ownerId, ownerNick, guestId, guestNick)
                : await _games.CreateGameAsync(room, guestId, guestNick, ownerId, ownerNick);
        }
        catch
        {
            lock (room.Sync) room.Status = RoomStatus.ReadyCheck;
            throw;
        }

        RoomDto roomDto;
        lock (room.Sync)
        {
            room.CurrentGameId = game.Id;
            roomDto = RoomMapper.ToDto(room, _presence);
        }

        var state = _games.ToDto(game, null);
        await _notifier.ToRoomAsync(code, Events.GameStarted, state);
        await _notifier.ToRoomAsync(code, Events.RoomUpdated, roomDto);
        _logger.LogInformation("Phòng {Code} bắt đầu ván {GameId}: Đỏ {Red} – Đen {Black}", code, game.Id, game.RedNickname, game.BlackNickname);
        return state;
    }

    // ------------------------------------------------------------------ chat

    public async Task<ChatMessageDto> SendChatAsync(int userId, string nickname, string code, string? content)
    {
        var room = Require(code);
        string text = (content ?? "").Trim();
        if (text.Length == 0 || text.Length > 500)
            throw new AppException(ErrorCodes.ChatInvalid, "Tin nhắn dài 1–500 ký tự.");

        ChatMessageDto msg;
        lock (room.Sync)
        {
            if (!room.IsMember(userId)) throw AppException.Forbidden(ErrorCodes.RoomNotMember, "Bạn không ở trong phòng này.");
            var now = DateTime.UtcNow;
            if (room.LastChatAt.TryGetValue(userId, out var last) && now - last < TimeSpan.FromMilliseconds(400))
                throw new AppException(ErrorCodes.ChatTooFast, "Bạn gửi tin nhắn quá nhanh.");
            room.LastChatAt[userId] = now;
            msg = new ChatMessageDto(++room.NextMessageId, code, userId, nickname, text, now);
            room.Messages.Add(msg);
            if (room.Messages.Count > Room.MaxChatHistory) room.Messages.RemoveAt(0);
        }
        await _notifier.ToRoomAsync(code, Events.ChatMessage, msg);
        return msg;
    }

    public List<ChatMessageDto> GetMessages(int userId, string code)
    {
        var room = Require(code);
        lock (room.Sync)
        {
            if (!room.IsMember(userId)) throw AppException.Forbidden(ErrorCodes.RoomNotMember, "Bạn không ở trong phòng này.");
            return room.Messages.ToList();
        }
    }

    public bool IsMember(int userId, string code)
    {
        var room = _registry.Get(code);
        if (room is null) return false;
        lock (room.Sync) return room.IsMember(userId);
    }

    // ------------------------------------------------------------------ dọn dẹp (gọi từ GameMaintenanceService)

    /// <summary>Người chơi mất kết nối quá 60 giây khi phòng KHÔNG có ván → tự rời phòng (chuyển chủ phòng / đóng phòng).</summary>
    public async Task CleanupIdleMembersAsync(DateTime now)
    {
        foreach (var room in _registry.All.ToList())
        {
            List<int> toRemove;
            lock (room.Sync)
            {
                if (room.Status == RoomStatus.Playing) continue;
                toRemove = room.Members.Where(id =>
                {
                    var since = _presence.OfflineSince(id);
                    if (since is null && !_presence.IsOnline(id)) since = room.CreatedAt; // chưa từng kết nối hub
                    return since is DateTime s && now - s > IdleLeaveAfter;
                }).ToList();
            }
            foreach (var id in toRemove) await LeaveAsync(id, room.Code);
        }
    }

    public async Task BroadcastRoomOfUserAsync(int userId)
    {
        var room = _registry.FindByUser(userId);
        if (room is null) return;
        RoomDto dto;
        lock (room.Sync) dto = RoomMapper.ToDto(room, _presence);
        await _notifier.ToRoomAsync(room.Code, Events.RoomUpdated, dto);
    }

    // ------------------------------------------------------------------ tiện ích

    private Room Require(string code) =>
        _registry.Get(code?.Trim() ?? "") ?? throw AppException.NotFound(ErrorCodes.RoomNotFound, "Phòng không tồn tại hoặc đã đóng.");

    private static RoomConfigDto Normalize(RoomConfigDto c)
    {
        string color = (c.OwnerColor ?? "RANDOM").ToUpperInvariant();
        if (color is not ("RED" or "BLACK" or "RANDOM")) color = "RANDOM";
        int total = AllowedTotalMinutes.Contains(c.TotalTimeMin) ? c.TotalTimeMin : Math.Clamp(c.TotalTimeMin, 1, 60);
        int move = Math.Clamp(c.MoveTimeSec, 10, 300);
        return new RoomConfigDto { OwnerColor = color, TotalTimeMin = total, MoveTimeSec = move, IsPrivate = c.IsPrivate };
    }
}
