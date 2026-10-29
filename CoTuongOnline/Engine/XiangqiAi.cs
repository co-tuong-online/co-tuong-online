using System.Diagnostics;

namespace CoTuongOnline.Engine;

public static class AiLevels
{
    public const string Easy = "EASY";
    public const string Hard = "HARD";

    public static bool IsValid(string? level) => level is Easy or Hard;
}

/// <summary>
/// Máy chơi cờ.
/// - EASY: nhìn 1 nước, cộng nhiễu ngẫu nhiên lớn → hay bỏ sót, phù hợp người mới.
/// - HARD: Alpha-beta (negamax) + đào sâu dần + tìm kiếm tĩnh (quiescence) + sắp xếp nước MVV-LVA,
///         giới hạn thời gian. Mỗi lần gọi dùng bản sao thế cờ nên an toàn khi chạy song song.
/// </summary>
public sealed class XiangqiAi
{
    private const int Mate = 100_000;
    private const int MaxPly = 64; // giới hạn độ sâu đệ quy (chống chuỗi chiếu qua chiếu lại vô tận)
    private const int Inf = 1_000_000;

    private static readonly int[] PieceValue = { 0, 0, 200, 200, 400, 900, 450, 100 };

    // Bảng điểm vị trí theo góc nhìn quân Đỏ (row 0 = phía đối phương). Quân Đen dùng hàng đối xứng 9 - row.
    private static readonly int[,] PawnTable =
    {
        {  0,  3,  6,  9, 12,  9,  6,  3,  0 },
        { 18, 36, 56, 80,120, 80, 56, 36, 18 },
        { 14, 26, 42, 60, 80, 60, 42, 26, 14 },
        { 10, 20, 30, 34, 40, 34, 30, 20, 10 },
        {  6, 12, 18, 18, 20, 18, 18, 12,  6 },
        {  2,  0,  8,  0,  8,  0,  8,  0,  2 },
        {  0,  0, -2,  0,  4,  0, -2,  0,  0 },
        {  0,  0,  0,  0,  0,  0,  0,  0,  0 },
        {  0,  0,  0,  0,  0,  0,  0,  0,  0 },
        {  0,  0,  0,  0,  0,  0,  0,  0,  0 },
    };
    private static readonly int[,] HorseTable =
    {
        {  4,  8, 16, 12,  4, 12, 16,  8,  4 },
        {  4, 10, 28, 16,  8, 16, 28, 10,  4 },
        { 12, 14, 16, 20, 18, 20, 16, 14, 12 },
        {  8, 24, 18, 24, 20, 24, 18, 24,  8 },
        {  6, 16, 14, 18, 16, 18, 14, 16,  6 },
        {  4, 12, 16, 14, 12, 14, 16, 12,  4 },
        {  2,  6,  8,  6, 10,  6,  8,  6,  2 },
        {  4,  2,  8,  8,  4,  8,  8,  2,  4 },
        {  0,  2,  4,  4, -2,  4,  4,  2,  0 },
        {  0, -4,  0,  0,  0,  0,  0, -4,  0 },
    };
    private static readonly int[,] RookTable =
    {
        { 14, 14, 12, 18, 16, 18, 12, 14, 14 },
        { 16, 20, 18, 24, 26, 24, 18, 20, 16 },
        { 12, 12, 12, 18, 18, 18, 12, 12, 12 },
        { 12, 18, 16, 22, 22, 22, 16, 18, 12 },
        { 12, 14, 12, 18, 18, 18, 12, 14, 12 },
        { 12, 16, 14, 20, 20, 20, 14, 16, 12 },
        {  6, 10,  8, 14, 14, 14,  8, 10,  6 },
        {  4,  8,  6, 14, 12, 14,  6,  8,  4 },
        {  8,  4,  8, 16,  8, 16,  8,  4,  8 },
        { -2, 10,  6, 14, 12, 14,  6, 10, -2 },
    };
    private static readonly int[,] CannonTable =
    {
        {  6,  4,  0,-10,-12,-10,  0,  4,  6 },
        {  2,  2,  0, -4,-14, -4,  0,  2,  2 },
        {  2,  2,  0,-10, -8,-10,  0,  2,  2 },
        {  0,  0, -2,  4, 10,  4, -2,  0,  0 },
        {  0,  0,  0,  2,  8,  2,  0,  0,  0 },
        { -2,  0,  4,  2,  6,  2,  4,  0, -2 },
        {  0,  0,  0,  2,  4,  2,  0,  0,  0 },
        {  4,  0,  8,  6, 10,  6,  8,  0,  4 },
        {  0,  2,  4,  6,  6,  6,  4,  2,  0 },
        {  0,  0,  2,  6,  6,  6,  2,  0,  0 },
    };

    private readonly Random _random;
    private Stopwatch _clock = new();
    private long _deadlineMs;
    private bool _aborted;
    private long _nodes;
    private readonly Move[,] _killers = new Move[64, 2];

    public XiangqiAi(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    // Thế cờ đã xuất hiện trong ván (từ XiangqiMatch.PositionCounts). Nước nào dẫn tới thế đã gặp 2 lần
    // sẽ thành hoà do lặp nước → máy chấm điểm 0 cho nước đó (chỉ chọn khi đang bị thua).
    private IReadOnlyDictionary<string, int>? _seen;

    public Move ChooseMove(Position position, string level, int timeLimitMs = 1500,
        IReadOnlyDictionary<string, int>? positionCounts = null)
    {
        _seen = positionCounts;
        var pos = position.Clone();
        var legal = pos.GenerateLegalMoves();
        if (legal.Count == 0) return Move.None;
        if (legal.Count == 1) return legal[0];
        try
        {
            var move = level == AiLevels.Hard ? SearchHard(pos, legal, timeLimitMs) : PickEasy(pos, legal);
            return legal.Contains(move) ? move : legal[0];
        }
        catch (Exception) when (legal.Count > 0)
        {
            // Không bao giờ để lỗi trong lúc tìm nước làm treo ván: đi tạm một nước hợp lệ
            return legal[_random.Next(legal.Count)];
        }
    }

    // ------------------------------------------------------------------ EASY

    private Move PickEasy(Position pos, List<Move> legal)
    {
        // 20% đi ngẫu nhiên hoàn toàn, còn lại chọn theo điểm 1 nước + nhiễu ±180
        if (_random.NextDouble() < 0.2) return legal[_random.Next(legal.Count)];
        int me = pos.SideToMove;
        Move best = legal[0];
        int bestScore = -Inf;
        foreach (var m in legal)
        {
            int cap = pos.MakeMove(m);
            int score = !pos.HasLegalMove() ? Mate : IsRepetitionDraw(pos) ? 0 : Evaluate(pos, me);
            pos.UnmakeMove(m, cap);
            score += _random.Next(-180, 181);
            if (score > bestScore) { bestScore = score; best = m; }
        }
        return best;
    }

    // ------------------------------------------------------------------ HARD

    private Move SearchHard(Position pos, List<Move> rootMoves, int timeLimitMs)
    {
        _clock = Stopwatch.StartNew();
        _deadlineMs = timeLimitMs;
        _aborted = false;
        _nodes = 0;
        Array.Clear(_killers);

        // Xáo trộn nhẹ để máy không đi giống hệt nhau mỗi ván
        rootMoves = rootMoves.OrderBy(_ => _random.Next()).ToList();
        OrderMoves(pos, rootMoves, 0, Move.None);

        Move best = rootMoves[0];
        for (int depth = 1; depth <= 12; depth++)
        {
            int alpha = -Inf, beta = Inf;
            Move iterationBest = Move.None;
            int iterationScore = -Inf;
            foreach (var m in rootMoves)
            {
                int cap = pos.MakeMove(m);
                int score = IsRepetitionDraw(pos) ? 0 : -Negamax(pos, depth - 1, -beta, -alpha, 1);
                pos.UnmakeMove(m, cap);
                if (_aborted) break;
                if (score > iterationScore) { iterationScore = score; iterationBest = m; }
                if (score > alpha) alpha = score;
            }
            if (_aborted) break;
            best = iterationBest;
            // Đưa nước tốt nhất lên đầu cho vòng sau
            rootMoves.Remove(best);
            rootMoves.Insert(0, best);
            if (iterationScore >= Mate - 64) break; // đã thấy chiếu bí
            if (_clock.ElapsedMilliseconds > _deadlineMs / 2) break; // không đủ thời gian cho vòng sâu hơn
        }
        return best;
    }

    private bool IsRepetitionDraw(Position pos) =>
        _seen is not null && _seen.TryGetValue(pos.Key(), out int count) && count >= XiangqiMatch.RepetitionDrawCount - 1;

    private int Negamax(Position pos, int depth, int alpha, int beta, int ply)
    {
        if ((++_nodes & 1023) == 0 && _clock.ElapsedMilliseconds > _deadlineMs) _aborted = true;
        if (_aborted) return 0;

        if (ply >= MaxPly) return Evaluate(pos, pos.SideToMove);

        int me = pos.SideToMove;
        bool inCheck = pos.IsInCheck(me);
        if (inCheck && ply < MaxPly / 2) depth++; // gia hạn khi bị chiếu (có giới hạn)
        if (depth <= 0) return Quiescence(pos, alpha, beta, ply, 0);

        var moves = new List<Move>(48);
        pos.GeneratePseudoMoves(me, moves);
        OrderMoves(pos, moves, ply, Move.None);

        int legalCount = 0;
        int best = -Inf;
        foreach (var m in moves)
        {
            int cap = pos.MakeMove(m);
            if (pos.IsInCheck(me)) { pos.UnmakeMove(m, cap); continue; }
            legalCount++;
            int score = -Negamax(pos, depth - 1, -beta, -alpha, ply + 1);
            pos.UnmakeMove(m, cap);
            if (_aborted) return 0;
            if (score > best) best = score;
            if (score > alpha) alpha = score;
            if (alpha >= beta)
            {
                if (cap == 0 && ply < 64)
                {
                    _killers[ply, 1] = _killers[ply, 0];
                    _killers[ply, 0] = m;
                }
                break;
            }
        }
        if (legalCount == 0) return -Mate + ply; // hết nước đi = thua
        return best;
    }

    private int Quiescence(Position pos, int alpha, int beta, int ply, int qdepth)
    {
        if ((++_nodes & 1023) == 0 && _clock.ElapsedMilliseconds > _deadlineMs) _aborted = true;
        if (_aborted) return 0;

        int me = pos.SideToMove;
        int standPat = Evaluate(pos, me);
        if (standPat >= beta) return standPat;
        if (standPat > alpha) alpha = standPat;
        if (qdepth >= 8) return standPat;

        var captures = new List<Move>(16);
        pos.GeneratePseudoMoves(me, captures, capturesOnly: true);
        OrderMoves(pos, captures, -1, Move.None);
        foreach (var m in captures)
        {
            // Ăn Tướng nghĩa là đối phương đã đi nước không hợp lệ
            if (Pieces.TypeOf(pos.Cells[m.To]) == Pieces.King) return Mate - ply;
            int cap = pos.MakeMove(m);
            if (pos.IsInCheck(me)) { pos.UnmakeMove(m, cap); continue; }
            int score = -Quiescence(pos, -beta, -alpha, ply + 1, qdepth + 1);
            pos.UnmakeMove(m, cap);
            if (_aborted) return 0;
            if (score >= beta) return score;
            if (score > alpha) alpha = score;
        }
        return alpha;
    }

    private void OrderMoves(Position pos, List<Move> moves, int ply, Move first)
    {
        int Score(Move m)
        {
            if (m == first) return 1_000_000;
            int victim = pos.Cells[m.To];
            if (victim != 0)
                return 100_000 + PieceValue[Pieces.TypeOf(victim)] * 10 - PieceValue[Pieces.TypeOf(pos.Cells[m.From])] / 10;
            if (ply >= 0 && ply < 64)
            {
                if (m == _killers[ply, 0]) return 50_000;
                if (m == _killers[ply, 1]) return 40_000;
            }
            return PositionalGain(pos, m);
        }
        var scored = moves.Select(m => (m, s: Score(m))).OrderByDescending(x => x.s).Select(x => x.m).ToList();
        moves.Clear();
        moves.AddRange(scored);
    }

    private static int PositionalGain(Position pos, Move m)
    {
        int p = pos.Cells[m.From];
        return SquareValue(p, m.To) - SquareValue(p, m.From);
    }

    // ------------------------------------------------------------------ đánh giá

    /// <summary>Điểm thế cờ theo góc nhìn của bên color (dương = color đang ưu thế).</summary>
    public static int Evaluate(Position pos, int color)
    {
        int score = 0;
        for (int i = 0; i < 90; i++)
        {
            int p = pos.Cells[i];
            if (p == 0) continue;
            int v = PieceValue[Pieces.TypeOf(p)] + SquareValue(p, i);
            score += Pieces.ColorOf(p) == color ? v : -v;
        }
        return score;
    }

    private static int SquareValue(int piece, int index)
    {
        int row = Pieces.Row(index), col = Pieces.Col(index);
        if (piece < 0) row = 9 - row; // quân Đen: lật bảng
        return Pieces.TypeOf(piece) switch
        {
            Pieces.Pawn => PawnTable[row, col],
            Pieces.Horse => HorseTable[row, col],
            Pieces.Rook => RookTable[row, col],
            Pieces.Cannon => CannonTable[row, col],
            _ => 0
        };
    }
}
