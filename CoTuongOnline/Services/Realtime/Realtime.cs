using System.Collections.Concurrent;
using CoTuongOnline.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CoTuongOnline.Services.Realtime;

/// <summary>Tên sự kiện server phát cho client (client đăng ký bằng connection.on(...)).</summary>
public static class Events
{
    public const string RoomUpdated = "ROOM_UPDATED";
    public const string PlayerJoined = "PLAYER_JOINED";
    public const string PlayerLeft = "PLAYER_LEFT";
    public const string Kicked = "KICKED";
    public const string InviteReceived = "INVITE_RECEIVED";
    public const string InviteUpdated = "INVITE_UPDATED";
    public const string GameStarted = "GAME_STARTED";
    public const string MoveAccepted = "MOVE_ACCEPTED";
    public const string MoveRejected = "MOVE_REJECTED";
    public const string GameEnded = "GAME_ENDED";
    public const string PlayerOffline = "PLAYER_OFFLINE";
    public const string PlayerOnline = "PLAYER_ONLINE";
    public const string ChatMessage = "CHAT_MESSAGE";
    public const string CallRequest = "CALL_REQUEST";
    public const string CallAccept = "CALL_ACCEPT";
    public const string CallReject = "CALL_REJECT";
    public const string CallEnd = "CALL_END";
    public const string Offer = "OFFER";
    public const string Answer = "ANSWER";
    public const string IceCandidate = "ICE_CANDIDATE";
    public const string Error = "ERROR";
}

/// <summary>
/// Theo dõi ai đang online (một user có thể mở nhiều tab = nhiều connection).
/// Khi connection cuối cùng đóng, ghi lại thời điểm để xử lý ngưỡng 15 giây / 60 giây.
/// </summary>
public sealed class PresenceTracker
{
    private sealed class Entry
    {
        public readonly HashSet<string> Connections = new();
        public string Nickname = "";
        public DateTime? DisconnectedAt;
    }

    private readonly ConcurrentDictionary<int, Entry> _users = new();

    /// <returns>true nếu user vừa chuyển từ offline sang online.</returns>
    public bool Connected(int userId, string nickname, string connectionId)
    {
        var e = _users.GetOrAdd(userId, _ => new Entry());
        lock (e)
        {
            bool wasOffline = e.Connections.Count == 0;
            e.Connections.Add(connectionId);
            e.Nickname = nickname;
            e.DisconnectedAt = null;
            return wasOffline;
        }
    }

    /// <returns>true nếu user không còn connection nào.</returns>
    public bool Disconnected(int userId, string connectionId)
    {
        if (!_users.TryGetValue(userId, out var e)) return true;
        lock (e)
        {
            e.Connections.Remove(connectionId);
            if (e.Connections.Count == 0)
            {
                e.DisconnectedAt = DateTime.UtcNow;
                return true;
            }
            return false;
        }
    }

    public bool IsOnline(int userId)
    {
        if (!_users.TryGetValue(userId, out var e)) return false;
        lock (e) return e.Connections.Count > 0;
    }

    /// <summary>Thời điểm mất kết nối (null nếu đang online hoặc chưa từng kết nối).</summary>
    public DateTime? OfflineSince(int userId)
    {
        if (!_users.TryGetValue(userId, out var e)) return null;
        lock (e) return e.Connections.Count > 0 ? null : e.DisconnectedAt;
    }

    public int OnlineCount => _users.Values.Count(e => { lock (e) return e.Connections.Count > 0; });

    public List<(int UserId, string Nickname)> OnlineUsers() =>
        _users.Where(kv => { lock (kv.Value) return kv.Value.Connections.Count > 0; })
              .Select(kv => (kv.Key, kv.Value.Nickname)).ToList();
}

/// <summary>Gửi sự kiện SignalR từ bất kỳ service nào (kể cả background service).</summary>
public sealed class RealtimeNotifier
{
    private readonly IHubContext<GameHub> _hub;
    public RealtimeNotifier(IHubContext<GameHub> hub) => _hub = hub;

    public static string GroupName(string roomCode) => $"room:{roomCode}";

    public Task ToRoomAsync(string roomCode, string evt, object payload) =>
        _hub.Clients.Group(GroupName(roomCode)).SendAsync(evt, payload);

    public Task ToUserAsync(int userId, string evt, object payload) =>
        _hub.Clients.User(userId.ToString()).SendAsync(evt, payload);

    public Task RemoveFromRoomAsync(string connectionId, string roomCode) =>
        _hub.Groups.RemoveFromGroupAsync(connectionId, GroupName(roomCode));
}
