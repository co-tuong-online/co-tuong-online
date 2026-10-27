using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using CoTuongOnline.Common;
using CoTuongOnline.Dtos;
using CoTuongOnline.Engine;
using CoTuongOnline.Models;
using CoTuongOnline.Repositories;

namespace CoTuongOnline.Services.Games;

public sealed class AiGame
{
    public long Id { get; set; }
    public int UserId { get; init; }
    public string Level { get; init; } = AiLevels.Easy;
    public int PlayerColor { get; init; } = Pieces.Red;
    public required XiangqiMatch Match { get; init; }
    public string Status { get; set; } = "PLAYING";
    public string? Result { get; set; }
    public string? EndReason { get; set; }
    public int UndoUsed { get; set; }
    public long? SavedGameId { get; set; }
    public string? SavedGameName { get; set; }
    public bool Thinking { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public DateTime TurnStartedAt { get; set; } = DateTime.UtcNow;
    public object Sync { get; } = new();

    public int AiColor => -PlayerColor;
    public bool IsPlaying => Status == "PLAYING";
}

/// <summary>Ván chơi với máy (EASY / HARD). Toàn bộ luật và nước đi của máy chạy ở server.</summary>
public sealed class AiGameService
{
    public const int MaxUndo = 3;
    private const int HardThinkMs = 1500;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(3);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<long, AiGame> _games = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AiGameService> _logger;

    public AiGameService(IServiceScopeFactory scopes, ILogger<AiGameService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    // ------------------------------------------------------------------ tạo ván

    public async Task<AiGameDto> CreateAsync(int userId, string level, string? playerColor,
        BoardState? fromState = null, long? savedGameId = null, string? savedName = null)
    {
        level = level?.ToUpperInvariant() ?? AiLevels.Easy;
        if (!AiLevels.IsValid(level)) throw new AppException(ErrorCodes.ValidationError, "Độ khó chỉ có EASY hoặc HARD.");

        int color = (playerColor ?? "red").ToLowerInvariant() switch
        {
            "black" => Pieces.Black,
            "random" => RandomNumberGenerator.GetInt32(2) == 0 ? Pieces.Red : Pieces.Black,
            _ => Pieces.Red
        };

        XiangqiMatch match;
        try
        {
            match = fromState is null ? XiangqiMatch.NewGame() : XiangqiMatch.FromState(fromState);
        }
        catch (FormatException ex)
        {
            throw new AppException(ErrorCodes.ValidationError, $"Dữ liệu ván cờ không hợp lệ: {ex.Message}");
        }

        var game = new AiGame
        {
            UserId = userId,
            Level = level,
            PlayerColor = color,
            Match = match,
            SavedGameId = savedGameId,
            SavedGameName = savedName
        };

        using (var scope = _scopes.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            var record = await repo.AddAsync(new GameRecord
            {
                RedPlayerId = color == Pieces.Red ? userId : null,
                BlackPlayerId = color == Pieces.Black ? userId : null,
                OpponentType = "AI",
                AiLevel = level,
                Status = "PLAYING",
                CurrentStateJson = JsonSerializer.Serialize(match.ToState(), Json)
            });
            game.Id = record.Id;
        }
        _games[game.Id] = game;
        _logger.LogInformation("User {User} bắt đầu ván {Id} với máy ({Level}), cầm {Color}", userId, game.Id, level, Pieces.ColorName(color));
        return ToDto(game);
    }

    public async Task<AiGameDto> GetAsync(int userId, long gameId) => ToDto(await RequireAsync(userId, gameId));

    // ------------------------------------------------------------------ nước của người

    public async Task<AiMoveResult> PlayerMoveAsync(int userId, long gameId, Coord from, Coord to)
    {
        var game = await RequireAsync(userId, gameId);
        MoveOutcome outcome;
        lock (game.Sync)
        {
            if (!game.IsPlaying) throw new AppException(ErrorCodes.GameFinished, "Ván cờ đã kết thúc.");
            if (game.Thinking || game.Match.SideToMove != game.PlayerColor)
                throw new AppException(ErrorCodes.GameNotYourTurn, "Chưa đến lượt bạn, máy đang nghĩ.");
            long spent = (long)(DateTime.UtcNow - game.TurnStartedAt).TotalMilliseconds;
            outcome = game.Match.TryMove(from, to, spent);
            if (!outcome.Success) throw new AppException(ErrorCodes.GameIllegalMove, outcome.Error ?? "Nước đi không hợp lệ.");
            game.TurnStartedAt = DateTime.UtcNow;
            game.LastActivity = DateTime.UtcNow;
            if (outcome.Ended is not null) Finish(game, outcome.Ended.Result, outcome.Ended.Reason);
        }
        await AfterMoveAsync(game, outcome.Record!);
        return new AiMoveResult(outcome.Record, ToDto(game));
    }

    // ------------------------------------------------------------------ nước của máy

    public async Task<AiMoveResult> AiMoveAsync(int userId, long gameId)
    {
        var game = await RequireAsync(userId, gameId);
        Position snapshot;
        Dictionary<string, int> seen;
        int plyBefore;
        lock (game.Sync)
        {
            if (!game.IsPlaying || game.Match.SideToMove != game.AiColor || game.Thinking)
                return new AiMoveResult(null, ToDto(game));
            game.Thinking = true;
            snapshot = game.Match.Position.Clone();
            seen = new Dictionary<string, int>(game.Match.PositionCounts);
            plyBefore = game.Match.Ply;
        }

        Move move;
        var started = DateTime.UtcNow;
        try
        {
            // Tìm nước trên thread pool để không chặn request khác
            move = await Task.Run(() => new XiangqiAi().ChooseMove(snapshot, game.Level, HardThinkMs, seen));
        }
        catch
        {
            lock (game.Sync) game.Thinking = false;
            throw;
        }

        MoveOutcome? outcome = null;
        lock (game.Sync)
        {
            game.Thinking = false;
            // Ván có thể đã bị đầu hàng/đi lại trong lúc máy nghĩ
            if (game.IsPlaying && game.Match.Ply == plyBefore && !move.IsNone)
            {
                outcome = game.Match.Apply(move, (long)(DateTime.UtcNow - started).TotalMilliseconds);
                game.TurnStartedAt = DateTime.UtcNow;
                game.LastActivity = DateTime.UtcNow;
                if (outcome.Ended is not null) Finish(game, outcome.Ended.Result, outcome.Ended.Reason);
            }
        }
        if (outcome?.Record is not null) await AfterMoveAsync(game, outcome.Record);
        return new AiMoveResult(outcome?.Record, ToDto(game));
    }

    // ------------------------------------------------------------------ đi lại / đầu hàng

    public async Task<AiGameDto> UndoAsync(int userId, long gameId)
    {
        var game = await RequireAsync(userId, gameId);
        lock (game.Sync)
        {
            if (!game.IsPlaying) throw new AppException(ErrorCodes.GameFinished, "Ván cờ đã kết thúc.");
            if (game.Thinking) throw new AppException(ErrorCodes.UndoNotAllowed, "Máy đang nghĩ, hãy chờ một chút.");
            if (game.UndoUsed >= MaxUndo) throw new AppException(ErrorCodes.UndoNotAllowed, $"Mỗi ván chỉ được đi lại {MaxUndo} lần.");

            // Lùi về lượt của người chơi: thường là 2 nửa nước (nước máy + nước mình)
            int steps = game.Match.SideToMove == game.PlayerColor ? 2 : 1;
            if (game.Match.UndoDepth < steps)
                throw new AppException(ErrorCodes.UndoNotAllowed, "Không còn nước nào để đi lại.");
            for (int i = 0; i < steps; i++) game.Match.Undo();
            game.UndoUsed++;
            game.TurnStartedAt = DateTime.UtcNow;
            return ToDto(game);
        }
    }

    public async Task<AiGameDto> ResignAsync(int userId, long gameId)
    {
        var game = await RequireAsync(userId, gameId);
        lock (game.Sync)
        {
            if (!game.IsPlaying) throw new AppException(ErrorCodes.GameFinished, "Ván cờ đã kết thúc.");
            Finish(game, GameResults.WinFor(game.AiColor), EndReasons.Resign);
        }
        await PersistAsync(game);
        return ToDto(game);
    }

    // ------------------------------------------------------------------ dùng cho lưu ván

    public async Task<(AiGame Game, BoardState State)> GetForSaveAsync(int userId, long gameId)
    {
        var game = await RequireAsync(userId, gameId);
        lock (game.Sync)
        {
            if (!game.IsPlaying) throw new AppException(ErrorCodes.GameFinished, "Ván đã kết thúc, không cần lưu.");
            if (game.Thinking) throw new AppException(ErrorCodes.GameNotYourTurn, "Máy đang nghĩ, hãy lưu sau nước của máy.");
            return (game, game.Match.ToState());
        }
    }

    public void MarkSaved(long gameId, long savedId, string name)
    {
        if (_games.TryGetValue(gameId, out var game))
            lock (game.Sync)
            {
                game.SavedGameId = savedId;
                game.SavedGameName = name;
            }
    }

    public void ForgetSave(long savedId)
    {
        foreach (var g in _games.Values.Where(g => g.SavedGameId == savedId))
            lock (g.Sync)
            {
                g.SavedGameId = null;
                g.SavedGameName = null;
            }
    }

    public void Cleanup(DateTime now)
    {
        foreach (var g in _games.Values)
            if (now - g.LastActivity > IdleTimeout) _games.TryRemove(g.Id, out _);
    }

    // ------------------------------------------------------------------ nội bộ

    /// <summary>
    /// Lấy ván đang chơi. Không còn trong RAM (server vừa khởi động lại, hoặc bị dọn vì lâu không chơi)
    /// thì nạp lại từ bảng games (cột CurrentStateJson) để người chơi đánh tiếp đúng thế cờ cũ.
    /// </summary>
    private async Task<AiGame> RequireAsync(int userId, long gameId)
    {
        if (!_games.TryGetValue(gameId, out var game))
        {
            game = await LoadFromDatabaseAsync(gameId)
                   ?? throw AppException.NotFound(ErrorCodes.GameNotFound, "Không tìm thấy ván cờ.");
            game = _games.GetOrAdd(game.Id, game);
        }
        if (game.UserId != userId) throw OnlineGameService.NotPlayer();
        return game;
    }

    private async Task<AiGame?> LoadFromDatabaseAsync(long gameId)
    {
        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        var record = await repo.GetByIdAsync(gameId);
        if (record is null || record.OpponentType != "AI") return null;

        int? owner = record.RedPlayerId ?? record.BlackPlayerId;
        if (owner is not int userId) return null;

        XiangqiMatch match;
        try
        {
            var state = JsonSerializer.Deserialize<BoardState>(record.CurrentStateJson, Json);
            if (state is null) return null;
            match = XiangqiMatch.FromState(state);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            _logger.LogWarning(ex, "Trạng thái ván máy {Id} trong DB bị hỏng", gameId);
            return null;
        }

        var game = new AiGame
        {
            Id = record.Id,
            UserId = userId,
            Level = AiLevels.IsValid(record.AiLevel) ? record.AiLevel! : AiLevels.Easy,
            PlayerColor = record.RedPlayerId == userId ? Pieces.Red : Pieces.Black,
            Match = match,
            Status = record.Status == "PLAYING" ? "PLAYING" : "FINISHED",
            Result = record.Result,
            EndReason = record.EndReason
        };
        if (!game.IsPlaying && game.Result is not null) match.ForceEnd(game.Result, game.EndReason ?? "");
        _logger.LogInformation("Nạp lại ván máy {Id} từ database ({Moves} nước)", gameId, match.Moves.Count);
        return game;
    }

    private static void Finish(AiGame game, string result, string reason)
    {
        game.Status = "FINISHED";
        game.Result = result;
        game.EndReason = reason;
        game.Match.ForceEnd(result, reason);
    }

    private async Task AfterMoveAsync(AiGame game, MoveRecord move)
    {
        bool finished;
        string stateJson;
        lock (game.Sync)
        {
            finished = !game.IsPlaying;
            stateJson = JsonSerializer.Serialize(game.Match.ToState(), Json);
        }
        try
        {
            using var scope = _scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            await repo.RecordMoveAsync(OnlineGameService.ToMoveRecord(game.Id, move), stateJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không ghi được nước đi ván máy {Id}", game.Id);
        }
        if (finished) await PersistAsync(game); // ghi kết quả + xoá bản lưu nếu có
    }

    private async Task PersistAsync(AiGame game)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            var record = await repo.GetByIdAsync(game.Id);
            if (record is not null)
            {
                lock (game.Sync)
                {
                    record.CurrentStateJson = JsonSerializer.Serialize(game.Match.ToState(), Json);
                    record.Status = game.Status;
                    record.Result = game.Result;
                    record.EndReason = game.EndReason;
                    if (!game.IsPlaying) record.EndedAt ??= DateTime.UtcNow;
                }
                await repo.UpdateAsync(record);
            }

            // Đặc tả: ván tải từ bản lưu được chơi tới kết thúc → bản lưu tự xoá
            long? savedId;
            lock (game.Sync) savedId = game.IsPlaying ? null : game.SavedGameId;
            if (savedId is long sid)
            {
                var saves = scope.ServiceProvider.GetRequiredService<ISavedGameRepository>();
                await saves.DeleteAsync(sid);
                lock (game.Sync)
                {
                    game.SavedGameId = null;
                    game.SavedGameName = null;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không ghi được trạng thái ván máy {Id}", game.Id);
        }
    }

    private static AiGameDto ToDto(AiGame g)
    {
        lock (g.Sync)
        {
            return new AiGameDto(
                g.Id, g.Level, Pieces.ColorName(g.PlayerColor), g.Status, g.Result, g.EndReason,
                g.Match.ToState(),
                g.IsPlaying && g.Match.SideToMove == g.PlayerColor ? g.Match.LegalMoves() : new List<LegalMove>(),
                g.IsPlaying && g.Match.InCheck,
                g.IsPlaying && g.UndoUsed < MaxUndo && g.Match.UndoDepth >= (g.Match.SideToMove == g.PlayerColor ? 2 : 1),
                MaxUndo - g.UndoUsed,
                g.SavedGameId,
                g.SavedGameName);
        }
    }
}
