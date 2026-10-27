using CoTuongOnline.Common;
using CoTuongOnline.Engine;
using CoTuongOnline.Services.Games;
using CoTuongOnline.Services.Realtime;
using CoTuongOnline.Services.Rooms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CoTuongOnline.Hubs;

/// <summary>Kết quả trả về cho connection.invoke(...) ở client.</summary>
public sealed record HubResult(bool Success, string Code, string? Message, object? Data = null)
{
    public static HubResult Ok(object? data = null) => new(true, "OK", null, data);
    public static HubResult Fail(string code, string? message) => new(false, code, message);
}

/// <summary>
/// Hub duy nhất của hệ thống (/hubs/game). Mỗi phòng là 1 Group "room:{code}".
/// Client gọi phương thức bằng connection.invoke("TenPhuongThuc", ...) và nhận sự kiện bằng connection.on("TEN_SU_KIEN", ...).
/// Sau mỗi lần tự kết nối lại (onreconnected) client phải gọi lại JoinRoom(code).
/// </summary>
[Authorize]
public sealed class GameHub : Hub
{
    private readonly PresenceTracker _presence;
    private readonly RoomService _rooms;
    private readonly InviteService _invites;
    private readonly OnlineGameService _games;
    private readonly ILogger<GameHub> _logger;

    public GameHub(PresenceTracker presence, RoomService rooms, InviteService invites, OnlineGameService games, ILogger<GameHub> logger)
    {
        _presence = presence;
        _rooms = rooms;
        _invites = invites;
        _games = games;
        _logger = logger;
    }

    private int UserId => Context.User!.GetUserId();
    private string Nickname => Context.User!.GetNickname();

    // ------------------------------------------------------------------ kết nối

    public override async Task OnConnectedAsync()
    {
        bool cameOnline = _presence.Connected(UserId, Nickname, Context.ConnectionId);
        if (cameOnline) await _rooms.BroadcastRoomOfUserAsync(UserId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        bool wentOffline = _presence.Disconnected(UserId, Context.ConnectionId);
        // Không xử lý thua ngay: GameMaintenanceService chờ 15 giây rồi mới đánh dấu OFFLINE, thêm 60 giây mới xử thua
        if (wentOffline) await _rooms.BroadcastRoomOfUserAsync(UserId);
        await base.OnDisconnectedAsync(exception);
    }

    // ------------------------------------------------------------------ phòng

    public async Task<HubResult> JoinRoom(string code)
    {
        return await Guard(async () =>
        {
            if (!_rooms.IsMember(UserId, code))
                throw AppException.Forbidden(ErrorCodes.RoomNotMember, "Bạn chưa vào phòng này.");
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeNotifier.GroupName(code));
            // Cập nhật trạng thái online cho người còn lại trong phòng
            await _rooms.BroadcastRoomOfUserAsync(UserId);
            return _rooms.Get(code);
        });
    }

    public async Task<HubResult> LeaveRoom(string code)
    {
        return await Guard(async () =>
        {
            await _rooms.LeaveAsync(UserId, code);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeNotifier.GroupName(code));
            return null;
        });
    }

    // ------------------------------------------------------------------ lời mời

    public async Task<HubResult> SendInvite(string code, int toUserId, string toNickname)
    {
        return await Guard(async () =>
        {
            var invite = await _invites.SendAsync(UserId, Nickname, code, toUserId, toNickname);
            return new { inviteId = invite.Id, expiresAt = invite.ExpiresAt };
        });
    }

    public async Task<HubResult> RespondInvite(long inviteId, bool accept)
    {
        return await Guard(async () =>
        {
            var code = await _invites.RespondAsync(UserId, Nickname, inviteId, accept);
            return new { code };
        });
    }

    // ------------------------------------------------------------------ ván online

    public async Task<HubResult> MakeMove(long gameId, Coord from, Coord to, int seq)
    {
        try
        {
            var result = await _games.MakeMoveAsync(UserId, gameId, from, to, seq);
            if (result.Accepted || result.Duplicate) return HubResult.Ok();
            await Clients.Caller.SendAsync(Events.MoveRejected, new
            {
                gameId,
                from,
                to,
                seq,
                code = result.Code,
                reason = result.Message
            });
            return HubResult.Fail(result.Code!, result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi MakeMove ván {GameId}", gameId);
            return HubResult.Fail(ErrorCodes.Internal, "Máy chủ gặp lỗi khi xử lý nước đi.");
        }
    }

    public async Task<HubResult> Resign(long gameId) =>
        await Guard(async () => { await _games.ResignAsync(UserId, gameId); return null; });

    // ------------------------------------------------------------------ chat

    public async Task<HubResult> SendChat(string code, string content) =>
        await Guard(async () => await _rooms.SendChatAsync(UserId, Nickname, code, content));

    // ------------------------------------------------------------------ video call (signaling WebRTC)
    // Server chỉ chuyển tiếp tín hiệu cho người còn lại trong phòng; luồng video đi trực tiếp P2P (STUN).

    public Task<HubResult> CallRequest(string code) => Relay(code, Events.CallRequest, new { fromUserId = UserId, fromNickname = Nickname });
    public Task<HubResult> CallAccept(string code) => Relay(code, Events.CallAccept, new { fromUserId = UserId, fromNickname = Nickname });
    public Task<HubResult> CallReject(string code) => Relay(code, Events.CallReject, new { fromUserId = UserId, fromNickname = Nickname });
    public Task<HubResult> CallEnd(string code) => Relay(code, Events.CallEnd, new { fromUserId = UserId, fromNickname = Nickname });
    public Task<HubResult> SendOffer(string code, string sdp) => Relay(code, Events.Offer, new { fromUserId = UserId, sdp });
    public Task<HubResult> SendAnswer(string code, string sdp) => Relay(code, Events.Answer, new { fromUserId = UserId, sdp });
    public Task<HubResult> SendIceCandidate(string code, object candidate) => Relay(code, Events.IceCandidate, new { fromUserId = UserId, candidate });

    private async Task<HubResult> Relay(string code, string evt, object payload)
    {
        if (!_rooms.IsMember(UserId, code))
            return HubResult.Fail(ErrorCodes.RoomNotMember, "Bạn không ở trong phòng này.");
        await Clients.OthersInGroup(RealtimeNotifier.GroupName(code)).SendAsync(evt, payload);
        return HubResult.Ok();
    }

    // ------------------------------------------------------------------ tiện ích

    private async Task<HubResult> Guard(Func<Task<object?>> action)
    {
        try
        {
            return HubResult.Ok(await action());
        }
        catch (AppException ex)
        {
            return HubResult.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi trong GameHub");
            return HubResult.Fail(ErrorCodes.Internal, "Máy chủ gặp lỗi, vui lòng thử lại.");
        }
    }
}
