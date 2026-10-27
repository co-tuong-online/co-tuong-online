using System.Collections.Concurrent;
using CoTuongOnline.Common;
using CoTuongOnline.Dtos;
using CoTuongOnline.Services.Realtime;

namespace CoTuongOnline.Services.Rooms;

public sealed class Invite
{
    public long Id { get; init; }
    public int FromUserId { get; init; }
    public string FromNickname { get; init; } = "";
    public int ToUserId { get; init; }
    public string ToNickname { get; init; } = "";
    public string Code { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
    public string Status { get; set; } = "PENDING"; // PENDING | ACCEPTED | DECLINED | EXPIRED | CANCELLED
}

/// <summary>Lời mời vào phòng theo biệt danh (gửi qua SignalR, hết hạn sau 30 giây).</summary>
public sealed class InviteService
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<long, Invite> _invites = new();
    private long _nextId;
    private readonly RoomService _rooms;
    private readonly PresenceTracker _presence;
    private readonly RealtimeNotifier _notifier;

    public InviteService(RoomService rooms, PresenceTracker presence, RealtimeNotifier notifier)
    {
        _rooms = rooms;
        _presence = presence;
        _notifier = notifier;
    }

    public async Task<Invite> SendAsync(int fromUserId, string fromNickname, string code, int toUserId, string toNickname)
    {
        if (fromUserId == toUserId) throw new AppException(ErrorCodes.InviteInvalid, "Không thể tự mời chính mình.");
        RoomDto room = _rooms.Get(code);
        if (room.OwnerId != fromUserId && room.Guest?.UserId != fromUserId)
            throw AppException.Forbidden(ErrorCodes.RoomNotMember, "Bạn không ở trong phòng này.");
        if (room.Status == RoomStatus.Playing) throw AppException.Conflict(ErrorCodes.RoomPlaying, "Phòng đang có ván.");
        if (room.Guest is not null) throw AppException.Conflict(ErrorCodes.RoomFull, "Phòng đã đủ 2 người.");
        if (!_presence.IsOnline(toUserId)) throw new AppException(ErrorCodes.UserOffline, $"{toNickname} hiện không online.");

        // Huỷ lời mời cũ còn hiệu lực từ cùng người mời tới cùng người nhận
        foreach (var old in _invites.Values.Where(i => i.Status == "PENDING" && i.FromUserId == fromUserId && i.ToUserId == toUserId))
            old.Status = "CANCELLED";

        var invite = new Invite
        {
            Id = Interlocked.Increment(ref _nextId),
            FromUserId = fromUserId,
            FromNickname = fromNickname,
            ToUserId = toUserId,
            ToNickname = toNickname,
            Code = code,
            ExpiresAt = DateTime.UtcNow + Ttl
        };
        _invites[invite.Id] = invite;

        await _notifier.ToUserAsync(toUserId, Events.InviteReceived, new
        {
            inviteId = invite.Id,
            fromUserId,
            fromNickname,
            code,
            totalTimeMin = room.Config.TotalTimeMin,
            moveTimeSec = room.Config.MoveTimeSec,
            expiresAt = invite.ExpiresAt
        });
        await _notifier.ToUserAsync(fromUserId, Events.InviteUpdated, Payload(invite));
        return invite;
    }

    /// <returns>Mã phòng nếu chấp nhận thành công, null nếu từ chối.</returns>
    public async Task<string?> RespondAsync(int userId, string nickname, long inviteId, bool accept)
    {
        if (!_invites.TryGetValue(inviteId, out var invite) || invite.ToUserId != userId)
            throw AppException.NotFound(ErrorCodes.InviteNotFound, "Lời mời không tồn tại.");
        if (invite.Status != "PENDING" || invite.ExpiresAt < DateTime.UtcNow)
            throw new AppException(ErrorCodes.InviteInvalid, "Lời mời đã hết hạn hoặc bị huỷ.");

        if (!accept)
        {
            invite.Status = "DECLINED";
            await _notifier.ToUserAsync(invite.FromUserId, Events.InviteUpdated, Payload(invite));
            return null;
        }

        try
        {
            await _rooms.JoinAsync(userId, nickname, invite.Code);
        }
        catch (AppException)
        {
            invite.Status = "CANCELLED";
            await _notifier.ToUserAsync(invite.FromUserId, Events.InviteUpdated, Payload(invite));
            throw;
        }
        invite.Status = "ACCEPTED";
        await _notifier.ToUserAsync(invite.FromUserId, Events.InviteUpdated, Payload(invite));
        return invite.Code;
    }

    public async Task ExpireAsync(DateTime now)
    {
        foreach (var invite in _invites.Values.ToList())
        {
            if (invite.Status == "PENDING" && invite.ExpiresAt < now)
            {
                invite.Status = "EXPIRED";
                await _notifier.ToUserAsync(invite.FromUserId, Events.InviteUpdated, Payload(invite));
                await _notifier.ToUserAsync(invite.ToUserId, Events.InviteUpdated, Payload(invite));
            }
            if (invite.Status != "PENDING" && now - invite.ExpiresAt > TimeSpan.FromMinutes(5))
                _invites.TryRemove(invite.Id, out _);
        }
    }

    private static object Payload(Invite i) => new
    {
        inviteId = i.Id,
        status = i.Status,
        code = i.Code,
        fromNickname = i.FromNickname,
        toNickname = i.ToNickname
    };
}
