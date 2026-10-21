using System.Security.Cryptography;
using CoTuongOnline.Data;
using CoTuongOnline.Features.Games;
using CoTuongOnline.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoTuongOnline.Features.Rooms;

public sealed record PlayerDto(string Id, string Username, string Color);
public sealed record RoomState(Guid Id, string Code, string HostId, PlayerDto[] Players, Board Board,
    string Status, long Revision, DateTimeOffset UpdatedAt);

public sealed class RoomService(AppDbContext db, UserManager<IdentityUser> users, IMovementEngine engine,
    IHubContext<GameHub> hub, ILogger<RoomService> logger)
{
    public static string Normalize(string code)
    {
        code = code.Trim().ToUpperInvariant();
        if (code.Length != 8 || code.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new ApiException(404, "Không tìm thấy phòng.");
        return code;
    }
    public IQueryable<Room> Query => db.Rooms.Include(r => r.Host).Include(r => r.BlackPlayer);
    public async Task<Room> Find(string code) => await Query.SingleOrDefaultAsync(r => r.Code == Normalize(code))
        ?? throw new ApiException(404, "Không tìm thấy phòng.");
    public static void RequireMember(Room room, string userId)
    {
        if (room.HostId != userId && room.BlackPlayerId != userId) throw new ApiException(403, "Bạn chưa tham gia phòng này.");
    }
    public static RoomState State(Room room)
    {
        var players = new List<PlayerDto> { new(room.HostId, room.Host.UserName!, "do") };
        if (room.BlackPlayerId is not null) players.Add(new(room.BlackPlayerId, room.BlackPlayer!.UserName!, "den"));
        var board = Board.Read(room.BoardJson);
        return new(room.Id, room.Code, room.HostId, players.ToArray(), board,
            room.BlackPlayerId is null ? "waiting" : board.Status, room.Revision, room.UpdatedAt);
    }
    public async Task<RoomState> Get(string code, string userId)
    {
        var room = await Query.AsNoTracking().SingleOrDefaultAsync(r => r.Code == Normalize(code))
            ?? throw new ApiException(404, "Không tìm thấy phòng.");
        RequireMember(room, userId); return State(room);
    }
    public async Task<RoomState[]> List(string userId) => (await Query.Where(r => r.HostId == userId || r.BlackPlayerId == userId)
        .OrderByDescending(r => r.UpdatedAt).ToListAsync()).Select(State).ToArray();
    public async Task<RoomState> Create(string userId)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int attempt = 0; attempt < 5; attempt++) {
            string code = new(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
            var room = new Room { Code = code, HostId = userId, Host = (await users.FindByIdAsync(userId))!, BoardJson = Board.Initial().Serialize() };
            db.Rooms.Add(room);
            try { await db.SaveChangesAsync(); return State(room); }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { db.Entry(room).State = EntityState.Detached; }
        }
        throw new ApiException(503, "Không thể tạo mã phòng. Vui lòng thử lại.");
    }
    public async Task<RoomState> Join(string code, string userId, Invitation? invitation = null)
    {
        var room = await Find(code);
        if (room.HostId == userId || room.BlackPlayerId == userId) return State(room);
        if (room.BlackPlayerId is not null) throw new ApiException(409, "Phòng đã đủ hai người chơi.");
        room.BlackPlayerId = userId;
        room.BlackPlayer = (await users.FindByIdAsync(userId))!;
        var pending = await db.Invitations.Where(i => i.RoomId == room.Id && i.Status == "pending").ToListAsync();
        foreach (var item in pending) item.Status = item.Id == invitation?.Id ? "accepted" : "closed";
        await Save(room);
        return State(room);
    }
    public async Task<RoomState> Move(string code, string userId, MoveRequest move)
    {
        var room = await Find(code); RequireMember(room, userId);
        if (room.BlackPlayerId is null) throw new ApiException(409, "Đang chờ người chơi thứ hai.");
        if (room.Revision != move.ExpectedRevision) throw new ApiException(409, "Bàn cờ đã thay đổi. Vui lòng tải lại.");
        try { room.BoardJson = engine.Move(Board.Read(room.BoardJson), room.HostId == userId ? "do" : "den", move.PieceId, move.Row, move.Col).Serialize(); }
        catch (InvalidOperationException e) { throw new ApiException(400, e.Message); }
        await Save(room);
        return State(room);
    }
    public async Task Invite(string code, string userId, string username)
    {
        var room = await Find(code);
        if (room.HostId != userId) throw new ApiException(403, "Chỉ chủ phòng được gửi lời mời.");
        if (room.BlackPlayerId is not null) throw new ApiException(409, "Phòng đã đủ hai người chơi.");
        var recipient = await users.FindByNameAsync(username.Trim()) ?? throw new ApiException(404, "Không tìm thấy tên đăng nhập.");
        if (recipient.Id == userId) throw new ApiException(400, "Không thể mời chính bạn.");
        var invite = await db.Invitations.SingleOrDefaultAsync(i => i.RoomId == room.Id && i.RecipientId == recipient.Id);
        if (invite is null) db.Invitations.Add(new Invitation { RoomId = room.Id, RecipientId = recipient.Id });
        else { invite.Status = "pending"; invite.CreatedAt = DateTimeOffset.UtcNow; }
        // Touch the room too, so sending an invite races safely with claiming the open seat.
        await Save(room);
    }
    private async Task Save(Room room)
    {
        room.Revision++; room.UpdatedAt = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ApiException(409, "Lời mời đã thay đổi. Vui lòng tải lại."); }
        try { await hub.Clients.Group(room.Code).SendAsync("RoomUpdated", State(room)); }
        catch (Exception e) { logger.LogWarning(e, "Room {Code} committed but notification failed", room.Code); }
    }
}
