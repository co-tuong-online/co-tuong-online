namespace CoTuongOnline.Engine;

public static class GameResults
{
    public const string RedWin = "RED_WIN";
    public const string BlackWin = "BLACK_WIN";
    public const string Draw = "DRAW";

    public static string WinFor(int color) => color == Pieces.Red ? RedWin : BlackWin;
}

public static class EndReasons
{
    public const string Checkmate = "CHECKMATE";         // chiếu bí
    public const string Stalemate = "STALEMATE";         // hết nước đi (bên hết nước thua)
    public const string Resign = "RESIGN";               // đầu hàng
    public const string Timeout = "TIMEOUT";             // hết giờ
    public const string Disconnect = "DISCONNECT";       // mất kết nối quá lâu
    public const string NoCaptureLimit = "NO_CAPTURE_LIMIT"; // 60 nước (120 ply) không ăn quân → hoà
    public const string Repetition = "REPETITION";             // cùng một thế cờ lặp lại 3 lần → hoà
}

public sealed record MatchOutcome(string Result, string Reason);

public sealed class MoveOutcome
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public MoveRecord? Record { get; init; }
    public bool Check { get; init; }
    public MatchOutcome? Ended { get; init; }

    public static MoveOutcome Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Một ván cờ: thế cờ + lịch sử nước đi + kiểm tra kết thúc. Dùng chung cho ván online và ván với máy.
/// Lớp này KHÔNG thread-safe; nơi gọi phải tự khoá.
/// </summary>
public sealed class XiangqiMatch
{
    public const int NoCaptureDrawPlies = 120;
    public const int RepetitionDrawCount = 3;

    private readonly Stack<(Move Move, int Captured, int PliesSinceCapture)> _history = new();

    // Lịch sử thế cờ (kể cả thế ban đầu) và số lần xuất hiện của mỗi thế → luật lặp nước
    private readonly List<string> _keys = new();
    private readonly Dictionary<string, int> _keyCounts = new();

    public Position Position { get; }
    public List<MoveRecord> Moves { get; }
    public int Ply { get; private set; }
    public MatchOutcome? Outcome { get; private set; }

    private XiangqiMatch(Position position, List<MoveRecord> moves, int ply)
    {
        Position = position;
        Moves = moves;
        Ply = ply;
    }

    public static XiangqiMatch NewGame()
    {
        var match = new XiangqiMatch(Position.Initial(), new List<MoveRecord>(), 0);
        match.PushKey(match.Position.Key());
        return match;
    }

    public static XiangqiMatch FromState(BoardState state)
    {
        var pos = Position.FromBoard(state.Board, state.SideToMove, state.PliesSinceCapture);
        var match = new XiangqiMatch(pos, state.Moves?.ToList() ?? new List<MoveRecord>(), state.Ply);
        match.RebuildPositionHistory();
        match.Outcome = match.DetectOutcome();
        return match;
    }

    /// <summary>
    /// Ván nạp từ JSON chỉ có thế cờ hiện tại + danh sách nước đi. Lùi ngược từng nước để dựng lại
    /// các thế cờ đã qua, nhờ vậy luật lặp nước vẫn đúng sau khi tải ván đã lưu hoặc khởi động lại server.
    /// </summary>
    private void RebuildPositionHistory()
    {
        var keys = new List<string> { Position.Key() };
        var p = Position.Clone();
        for (int i = Moves.Count - 1; i >= 0; i--)
        {
            var m = Moves[i];
            if (m.From is null || m.To is null || !m.From.IsValid || !m.To.IsValid) break;
            int from = m.From.ToIndex(), to = m.To.ToIndex();
            int piece;
            try { piece = Pieces.FromCode(m.Piece); }
            catch (FormatException) { break; }
            if (piece == 0 || p.Cells[to] != piece || p.Cells[from] != 0) break; // dữ liệu không khớp → dừng
            p.Cells[from] = piece;
            p.Cells[to] = Pieces.FromCode(m.Captured);
            p.SideToMove = -p.SideToMove;
            keys.Add(p.Key());
        }
        keys.Reverse();
        foreach (var k in keys) PushKey(k);
    }

    private void PushKey(string key)
    {
        _keys.Add(key);
        _keyCounts[key] = _keyCounts.GetValueOrDefault(key) + 1;
    }

    private void PopKey()
    {
        if (_keys.Count == 0) return;
        string key = _keys[^1];
        _keys.RemoveAt(_keys.Count - 1);
        if (--_keyCounts[key] == 0) _keyCounts.Remove(key);
    }

    /// <summary>Số lần mỗi thế cờ đã xuất hiện trong ván (máy dùng để tránh đi vào thế hoà do lặp nước).</summary>
    public IReadOnlyDictionary<string, int> PositionCounts => _keyCounts;

    public int SideToMove => Position.SideToMove;
    public bool IsOver => Outcome is not null;
    public bool InCheck => Position.IsInCheck(Position.SideToMove);
    public bool CanUndo => _history.Count > 0;
    public int UndoDepth => _history.Count;
    public MoveRecord? LastMove => Moves.Count > 0 ? Moves[^1] : null;

    public MoveOutcome TryMove(Coord from, Coord to, long spentMs)
    {
        if (IsOver) return MoveOutcome.Fail("Ván cờ đã kết thúc.");
        if (from is null || to is null || !from.IsValid || !to.IsValid) return MoveOutcome.Fail("Toạ độ nằm ngoài bàn cờ.");
        var move = new Move(from.ToIndex(), to.ToIndex());
        var error = Position.ValidateMove(move);
        if (error is not null) return MoveOutcome.Fail(error);
        return Apply(move, spentMs);
    }

    /// <summary>Đi một nước đã biết chắc là hợp lệ (nước của máy).</summary>
    public MoveOutcome Apply(Move move, long spentMs)
    {
        string notation = Notation.Describe(Position, move);
        int piece = Position.Cells[move.From];
        int prevNoCapture = Position.PliesSinceCapture;
        int captured = Position.MakeMove(move);
        Position.PliesSinceCapture = captured != 0 ? 0 : prevNoCapture + 1;
        _history.Push((move, captured, prevNoCapture));
        PushKey(Position.Key());
        Ply++;

        var record = new MoveRecord
        {
            Ply = Ply,
            From = Coord.FromIndex(move.From),
            To = Coord.FromIndex(move.To),
            Piece = Pieces.ToCode(piece)!,
            Captured = Pieces.ToCode(captured),
            Notation = notation,
            SpentMs = spentMs
        };
        Moves.Add(record);
        Outcome = DetectOutcome();
        return new MoveOutcome { Success = true, Record = record, Check = InCheck, Ended = Outcome };
    }

    /// <summary>Huỷ nước cuối (chỉ dùng cho ván với máy).</summary>
    public bool Undo()
    {
        if (_history.Count == 0) return false;
        var (move, captured, prevNoCapture) = _history.Pop();
        Position.UnmakeMove(move, captured);
        Position.PliesSinceCapture = prevNoCapture;
        PopKey();
        Moves.RemoveAt(Moves.Count - 1);
        Ply--;
        Outcome = null;
        return true;
    }

    /// <summary>Kết thúc ván vì lý do bên ngoài luật cờ (đầu hàng, hết giờ, mất kết nối).</summary>
    public void ForceEnd(string result, string reason) => Outcome ??= new MatchOutcome(result, reason);

    public List<LegalMove> LegalMoves()
    {
        if (IsOver) return new List<LegalMove>();
        return Position.GenerateLegalMoves()
            .Select(m => new LegalMove(Coord.FromIndex(m.From), Coord.FromIndex(m.To)))
            .ToList();
    }

    public BoardState ToState(ClockState? clock = null) => new()
    {
        Board = Position.ToBoard(),
        SideToMove = Pieces.ColorName(Position.SideToMove),
        Ply = Ply,
        PliesSinceCapture = Position.PliesSinceCapture,
        Clock = clock,
        Moves = Moves.ToList()
    };

    private MatchOutcome? DetectOutcome()
    {
        if (!Position.HasLegalMove())
        {
            // Bên tới lượt không còn nước đi → thua (luật cờ tướng: hết nước cũng là thua)
            int loser = Position.SideToMove;
            string reason = Position.IsInCheck(loser) ? EndReasons.Checkmate : EndReasons.Stalemate;
            return new MatchOutcome(GameResults.WinFor(-loser), reason);
        }
        // Luật rút gọn: cùng một thế cờ (cùng bên tới lượt) xuất hiện 3 lần → hoà.
        // (Luật thi đấu đầy đủ còn xử thua bên chiếu mãi / đuổi bắt mãi — nằm ngoài phạm vi đồ án.)
        if (_keys.Count > 0 && _keyCounts.GetValueOrDefault(_keys[^1]) >= RepetitionDrawCount)
            return new MatchOutcome(GameResults.Draw, EndReasons.Repetition);
        if (Position.PliesSinceCapture >= NoCaptureDrawPlies)
            return new MatchOutcome(GameResults.Draw, EndReasons.NoCaptureLimit);
        return null;
    }
}
