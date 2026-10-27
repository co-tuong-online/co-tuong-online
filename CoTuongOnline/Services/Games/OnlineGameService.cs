using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using CoTuongOnline.Common;
using CoTuongOnline.Dtos;
using CoTuongOnline.Engine;
using CoTuongOnline.Models;
using CoTuongOnline.Repositories;
using CoTuongOnline.Services.Realtime;
using CoTuongOnline.Services.Rooms;

namespace CoTuongOnline.Services.Games;

/// <summary>Ván online đang diễn ra (trạng thái sống trong RAM, mỗi nước được ghi xuống repository).</summary>
public sealed class OnlineGame
{
    public long Id { get; set; }
    public required string RoomCode { get; init; }
    public int RedId { get; init; }
    public string RedNickname { get; init; } = "";
    public int BlackId { get; init; }
    public string BlackNickname { get; init; } = "";
    public required XiangqiMatch Match { get; init; }
    public long RedMs { get; set; }
    public long BlackMs { get; set; }
    public int MoveTimeSec { get; init; }
    public DateTime TurnStartedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "PLAYING";
    public string? Result { get; set; }
    public string? EndReason { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public Dictionary<int, int> LastSeq { get; } = new();
    public int? OfflineUserId { get; set; }
    public DateTime? ForfeitAt { get; set; }
    public object Sync { get; } = new();

    public bool IsPlaying => Status == "PLAYING";
    public int ColorOf(int userId) => userId == RedId ? Pieces.Red : userId == BlackId ? Pieces.Black : 0;
    public int PlayerOf(int color) => color == Pieces.Red ? RedId : BlackId;
    public string NicknameOf(int userId) => userId == RedId ? RedNickname : BlackNickname;
}

public sealed record MoveAttemptResult(bool Accepted, bool Duplicate, string? Code, string? Message);

public sealed class OnlineGameService
{
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ForfeitAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<long, OnlineGame> _games = new();
    private readonly RoomRegistry _rooms;
    private readonly PresenceTracker _presence;
    private readonly RealtimeNotifier _notifier;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OnlineGameService> _logger;

    public OnlineGameService(RoomRegistry rooms, PresenceTracker presence, RealtimeNotifier notifier,
        IServiceScopeFactory scopes, ILogger<OnlineGameService> logger)
    {
        _rooms = rooms;
        _presence = presence;
        _notifier = notifier;
        _scopes = scopes;
        _logger = logger;
    }

    public OnlineGame? Get(long gameId) => _games.GetValueOrDefault(gameId);

    // ------------------------------------------------------------------ bắt đầu ván

    /// <summary>Tạo ván mới cho phòng (gọi bên trong lock của phòng). Trả về ván đã có Id.</summary>
    public async Task<OnlineGame> CreateGameAsync(Room room, int redId, string redNick, int blackId, string blackNick)
    {
        long totalMs = room.Config.TotalTimeMin * 60_000L;
        var game = new OnlineGame
        {
            RoomCode = room.Code,
            RedId = redId,
            RedNickname = redNick,
            BlackId = blackId,
            BlackNickname = blackNick,
            Match = XiangqiMatch.NewGame(),
            RedMs = totalMs,
            BlackMs = totalMs,
            MoveTimeSec = room.Config.MoveTimeSec,
        };

        using (var scope = _scopes.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            var record = await repo.AddAsync(new GameRecord
            {
                RoomCode = room.Code,
                RedPlayerId = redId,
                BlackPlayerId = blackId,
                OpponentType = "HUMAN",
                Status = "PLAYING",
                CurrentStateJson = JsonSerializer.Serialize(game.Match.ToState(Clock(game)), Json),
                StartedAt = game.StartedAt
            });
            game.Id = record.Id;
        }
        game.TurnStartedAt = DateTime.UtcNow;
        _games[game.Id] = game;
        return game;
    }

    // ------------------------------------------------------------------ đi quân

    public async Task<MoveAttemptResult> MakeMoveAsync(int userId, long gameId, Coord from, Coord to, int seq)
    {
        var game = Get(gameId);
        if (game is null) return new(false, false, ErrorCodes.GameNotFound, "Không tìm thấy ván cờ.");

        MoveOutcome outcome;
        object payload;
        lock (game.Sync)
        {
            if (!game.IsPlaying) return new(false, false, ErrorCodes.GameFinished, "Ván cờ đã kết thúc.");
            int color = game.ColorOf(userId);
            if (color == 0) return new(false, false, ErrorCodes.GameNotPlayer, "Bạn không phải người chơi của ván này.");

            // Chống gửi trùng (mạng chập chờn, bấm 2 lần): bỏ qua bản có cùng seq
            if (game.LastSeq.TryGetValue(userId, out var last) && last == seq) return new(false, true, null, null);
            game.LastSeq[userId] = seq;

            if (game.Match.SideToMove != color) return new(false, false, ErrorCodes.GameNotYourTurn, "Chưa đến lượt bạn.");

            var now = DateTime.UtcNow;
            long spent = (long)(now - game.TurnStartedAt).TotalMilliseconds;
            outcome = game.Match.TryMove(from, to, spent);
            if (!outcome.Success) return new(false, false, ErrorCodes.GameIllegalMove, outcome.Error);

            if (color == Pieces.Red) game.RedMs = Math.Max(0, game.RedMs - spent);
            else game.BlackMs = Math.Max(0, game.BlackMs - spent);
            game.TurnStartedAt = now;

            payload = new
            {
                gameId = game.Id,
                move = outcome.Record,
                board = game.Match.Position.ToBoard(),
                sideToMove = Pieces.ColorName(game.Match.SideToMove),
                ply = game.Match.Ply,
                clock = Clock(game),
                check = outcome.Check,
                legalMoves = game.Match.LegalMoves()
            };
        }

        await PersistMoveAsync(game, outcome.Record!);
        await _notifier.ToRoomAsync(game.RoomCode, Events.MoveAccepted, payload);
        if (outcome.Ended is not null)
            await EndGameAsync(game, outcome.Ended.Result, outcome.Ended.Reason);
        return new(true, false, null, null);
    }

    public async Task ResignAsync(int userId, long gameId)
    {
        var game = Get(gameId) ?? throw AppException.NotFound(ErrorCodes.GameNotFound, "Không tìm thấy ván cờ.");
        int color = game.ColorOf(userId);
        if (color == 0) throw AppException.Forbidden(ErrorCodes.GameNotPlayer, "Bạn không phải người chơi của ván này.");
        await EndGameAsync(game, GameResults.WinFor(-color), EndReasons.Resign);
    }

    // ------------------------------------------------------------------ kết thúc ván

    public async Task EndGameAsync(OnlineGame game, string result, string reason)
    {
        lock (game.Sync)
        {
            if (!game.IsPlaying) return;
            game.Status = "FINISHED";
            game.Result = result;
            game.EndReason = reason;
            game.EndedAt = DateTime.UtcNow;
            game.Match.ForceEnd(result, reason);
            // Trừ nốt thời gian của nước đang dở để đồng hồ hiển thị đúng
            long spent = (long)(game.EndedAt.Value - game.TurnStartedAt).TotalMilliseconds;
            if (game.Match.SideToMove == Pieces.Red) game.RedMs = Math.Max(0, game.RedMs - spent);
            else game.BlackMs = Math.Max(0, game.BlackMs - spent);
            game.TurnStartedAt = game.EndedAt.Value;
        }

        int? winnerId = result switch
        {
            GameResults.RedWin => game.RedId,
            GameResults.BlackWin => game.BlackId,
            _ => null
        };

        await PersistEndAsync(game, winnerId);

        // Phòng quay về trạng thái chờ để có thể đánh ván mới
        RoomDto? roomDto = null;
        var room = _rooms.Get(game.RoomCode);
        if (room is not null)
        {
            lock (room.Sync)
            {
                if (room.CurrentGameId == game.Id)
                {
                    room.CurrentGameId = null;
                    room.GuestReady = false;
                    room.Status = room.GuestId is null ? RoomStatus.Waiting : RoomStatus.ReadyCheck;
                }
                roomDto = RoomMapper.ToDto(room, _presence);
            }
        }

        await _notifier.ToRoomAsync(game.RoomCode, Events.GameEnded, new
        {
            gameId = game.Id,
            result,
            endReason = reason,
            winnerId,
            winnerNickname = winnerId is int w ? game.NicknameOf(w) : null,
            clock = Clock(game)
        });
        if (roomDto is not null) await _notifier.ToRoomAsync(game.RoomCode, Events.RoomUpdated, roomDto);
        _logger.LogInformation("Ván {GameId} phòng {Room} kết thúc: {Result} ({Reason})", game.Id, game.RoomCode, result, reason);
    }

    // ------------------------------------------------------------------ trạng thái

    public GameStateDto GetState(long gameId, int userId)
    {
        var game = Get(gameId) ?? throw AppException.NotFound(ErrorCodes.GameNotFound, "Không tìm thấy ván cờ (có thể ván đã kết thúc quá lâu).");
        int color = game.ColorOf(userId);
        if (color == 0) throw AppException.Forbidden(ErrorCodes.GameNotPlayer, "Bạn không phải người chơi của ván này.");
        return ToDto(game, Pieces.ColorName(color));
    }

    public GameStateDto ToDto(OnlineGame game, string? myColor)
    {
        lock (game.Sync)
        {
            return new GameStateDto(
                game.Id, "ONLINE", game.RoomCode, game.Status, game.Result, game.EndReason,
                new PlayerDto(game.RedId, game.RedNickname, _presence.IsOnline(game.RedId)),
                new PlayerDto(game.BlackId, game.BlackNickname, _presence.IsOnline(game.BlackId)),
                myColor,
                game.Match.ToState(Clock(game)),
                game.Match.LegalMoves(),
                game.IsPlaying && game.Match.InCheck,
                game.OfflineUserId is int off ? game.NicknameOf(off) : null,
                game.ForfeitAt);
        }
    }

    public OnlineGame? FindPlayingGameOf(int userId) =>
        _games.Values.FirstOrDefault(g => g.IsPlaying && g.ColorOf(userId) != 0);

    private static ClockState Clock(OnlineGame g) => new()
    {
        RedMs = g.RedMs,
        BlackMs = g.BlackMs,
        MoveTimeSec = g.MoveTimeSec,
        TurnElapsedMs = g.IsPlaying ? (long)(DateTime.UtcNow - g.TurnStartedAt).TotalMilliseconds : 0
    };

    // ------------------------------------------------------------------ bộ hẹn giờ (gọi từ GameMaintenanceService)

    public async Task TickAsync(DateTime now)
    {
        foreach (var game in _games.Values)
        {
            if (!game.IsPlaying)
            {
                if (game.EndedAt is DateTime ended && now - ended > KeepFinished) _games.TryRemove(game.Id, out _);
                continue;
            }

            // 1) Hết giờ: server tự phát hiện, không chờ client báo
            int side;
            long elapsed, remaining;
            lock (game.Sync)
            {
                side = game.Match.SideToMove;
                elapsed = (long)(now - game.TurnStartedAt).TotalMilliseconds;
                remaining = (side == Pieces.Red ? game.RedMs : game.BlackMs) - elapsed;
            }
            if (remaining <= 0 || elapsed > game.MoveTimeSec * 1000L)
            {
                await EndGameAsync(game, GameResults.WinFor(-side), EndReasons.Timeout);
                continue;
            }

            // 2) Mất kết nối: 15 giây → OFFLINE, thêm 60 giây → xử thua
            await CheckConnectionAsync(game, now);
        }
    }

    private async Task CheckConnectionAsync(OnlineGame game, DateTime now)
    {
        if (game.OfflineUserId is int offlineId)
        {
            if (_presence.IsOnline(offlineId))
            {
                game.OfflineUserId = null;
                game.ForfeitAt = null;
                await _notifier.ToRoomAsync(game.RoomCode, Events.PlayerOnline,
                    new { gameId = game.Id, userId = offlineId, nickname = game.NicknameOf(offlineId) });
            }
            else if (game.ForfeitAt is DateTime f && now >= f)
            {
                int color = game.ColorOf(offlineId);
                await EndGameAsync(game, GameResults.WinFor(-color), EndReasons.Disconnect);
            }
            return;
        }

        foreach (int userId in new[] { game.RedId, game.BlackId })
        {
            var since = _presence.OfflineSince(userId);
            bool neverConnected = since is null && !_presence.IsOnline(userId);
            if (neverConnected) since = game.StartedAt;
            if (since is DateTime s && now - s >= OfflineGrace)
            {
                game.OfflineUserId = userId;
                game.ForfeitAt = now + ForfeitAfter;
                await _notifier.ToRoomAsync(game.RoomCode, Events.PlayerOffline, new
                {
                    gameId = game.Id,
                    userId,
                    nickname = game.NicknameOf(userId),
                    forfeitAt = game.ForfeitAt
                });
                break;
            }
        }
    }

    // ------------------------------------------------------------------ ghi xuống repository

    private async Task PersistMoveAsync(OnlineGame game, MoveRecord move)
    {
        try
        {
            string stateJson;
            lock (game.Sync) stateJson = JsonSerializer.Serialize(game.Match.ToState(Clock(game)), Json);
            using var scope = _scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            await repo.RecordMoveAsync(ToMoveRecord(game.Id, move), stateJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không ghi được nước đi của ván {GameId}", game.Id);
        }
    }

    public static GameMoveRecord ToMoveRecord(long gameId, MoveRecord move) => new()
    {
        GameId = gameId,
        Ply = move.Ply,
        FromRow = move.From.Row, FromCol = move.From.Col,
        ToRow = move.To.Row, ToCol = move.To.Col,
        Piece = move.Piece,
        Captured = move.Captured,
        Notation = move.Notation,
        SpentMs = move.SpentMs
    };

    private async Task PersistEndAsync(OnlineGame game, int? winnerId)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var games = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

            var record = await games.GetByIdAsync(game.Id);
            if (record is not null)
            {
                record.Status = "FINISHED";
                record.Result = game.Result;
                record.EndReason = game.EndReason;
                record.EndedAt = game.EndedAt;
                lock (game.Sync) record.CurrentStateJson = JsonSerializer.Serialize(game.Match.ToState(Clock(game)), Json);
                await games.UpdateAsync(record);
            }

            foreach (var user in await users.GetByIdsAsync(new[] { game.RedId, game.BlackId }))
            {
                user.GamesPlayed++;
                if (winnerId is null) user.Draws++;
                else if (winnerId == user.Id) user.Wins++;
                else user.Losses++;
                await users.UpdateAsync(user);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không ghi được kết quả ván {GameId}", game.Id);
        }
    }

    public static AppException NotPlayer() =>
        new(ErrorCodes.GameNotPlayer, "Bạn không phải người chơi của ván này.", HttpStatusCode.Forbidden);
}
