namespace CoTuongOnline.Engine;

/// <summary>
/// Thế cờ: 90 ô + bên tới lượt. Sinh nước đi, kiểm tra chiếu, đi/huỷ nước.
/// Đây là "nguồn sự thật" về luật cờ: mọi nước đi từ client đều được kiểm tra ở đây.
/// </summary>
public sealed class Position
{
    public readonly int[] Cells = new int[90];
    public int SideToMove = Pieces.Red;
    public int PliesSinceCapture;

    private static readonly int[] OrthoDr = { -1, 1, 0, 0 };
    private static readonly int[] OrthoDc = { 0, 0, -1, 1 };
    private static readonly int[] DiagDr = { -1, -1, 1, 1 };
    private static readonly int[] DiagDc = { -1, 1, -1, 1 };
    // Mã: 8 hướng nhảy và vị trí "chân" tương ứng
    private static readonly int[] HorseDr = { -2, -2, 2, 2, -1, 1, -1, 1 };
    private static readonly int[] HorseDc = { -1, 1, -1, 1, -2, -2, 2, 2 };
    private static readonly int[] HorseLegDr = { -1, -1, 1, 1, 0, 0, 0, 0 };
    private static readonly int[] HorseLegDc = { 0, 0, 0, 0, -1, -1, 1, 1 };

    public static readonly string?[][] InitialBoard =
    {
        new[] { "bR", "bN", "bB", "bA", "bK", "bA", "bB", "bN", "bR" },
        new string?[9],
        new[] { null, "bC", null, null, null, null, null, "bC", null },
        new[] { "bP", null, "bP", null, "bP", null, "bP", null, "bP" },
        new string?[9],
        new string?[9],
        new[] { "rP", null, "rP", null, "rP", null, "rP", null, "rP" },
        new[] { null, "rC", null, null, null, null, null, "rC", null },
        new string?[9],
        new[] { "rR", "rN", "rB", "rA", "rK", "rA", "rB", "rN", "rR" },
    };

    public static Position Initial() => FromBoard(InitialBoard, "red", 0);

    public static Position FromBoard(string?[][] board, string? sideToMove, int pliesSinceCapture)
    {
        if (board is null || board.Length != 10 || board.Any(r => r is null || r.Length != 9))
            throw new FormatException("Bàn cờ phải có 10 hàng x 9 cột.");
        var pos = new Position { SideToMove = Pieces.ParseColor(sideToMove), PliesSinceCapture = pliesSinceCapture };
        int redKings = 0, blackKings = 0;
        for (int r = 0; r < 10; r++)
            for (int c = 0; c < 9; c++)
            {
                int p = Pieces.FromCode(board[r][c]);
                pos.Cells[Pieces.Index(r, c)] = p;
                if (p == Pieces.King) redKings++;
                if (p == -Pieces.King) blackKings++;
            }
        if (redKings != 1 || blackKings != 1)
            throw new FormatException("Mỗi bên phải có đúng một Tướng.");
        return pos;
    }

    public string?[][] ToBoard()
    {
        var board = new string?[10][];
        for (int r = 0; r < 10; r++)
        {
            board[r] = new string?[9];
            for (int c = 0; c < 9; c++) board[r][c] = Pieces.ToCode(Cells[Pieces.Index(r, c)]);
        }
        return board;
    }

    /// <summary>Chuỗi định danh thế cờ (91 ký tự: 90 ô + bên tới lượt) để phát hiện lặp nước.</summary>
    public string Key()
    {
        Span<char> buf = stackalloc char[91];
        for (int i = 0; i < 90; i++) buf[i] = (char)('H' + Cells[i]); // quân -7..7 → ký tự 'A'..'O'
        buf[90] = SideToMove == Pieces.Red ? 'r' : 'b';
        return new string(buf);
    }

    public Position Clone()
    {
        var p = new Position { SideToMove = SideToMove, PliesSinceCapture = PliesSinceCapture };
        Array.Copy(Cells, p.Cells, 90);
        return p;
    }

    // ------------------------------------------------------------------ đi / huỷ nước

    /// <summary>Đi nước (không kiểm tra luật). Trả về quân bị ăn (0 nếu không ăn).</summary>
    public int MakeMove(Move m)
    {
        int captured = Cells[m.To];
        Cells[m.To] = Cells[m.From];
        Cells[m.From] = Pieces.Empty;
        SideToMove = -SideToMove;
        return captured;
    }

    public void UnmakeMove(Move m, int captured)
    {
        Cells[m.From] = Cells[m.To];
        Cells[m.To] = captured;
        SideToMove = -SideToMove;
    }

    // ------------------------------------------------------------------ sinh nước đi

    /// <summary>Sinh nước đi giả hợp lệ (đúng cách đi của quân, chưa xét Tướng bị chiếu).</summary>
    public void GeneratePseudoMoves(int color, List<Move> moves, bool capturesOnly = false)
    {
        for (int i = 0; i < 90; i++)
        {
            int p = Cells[i];
            if (p == 0 || Pieces.ColorOf(p) != color) continue;
            GeneratePieceMoves(i, moves, capturesOnly);
        }
    }

    public void GeneratePieceMoves(int from, List<Move> moves, bool capturesOnly = false)
    {
        int p = Cells[from];
        int color = Pieces.ColorOf(p);
        int r = Pieces.Row(from), c = Pieces.Col(from);

        void TryAdd(int tr, int tc)
        {
            if (!Pieces.OnBoard(tr, tc)) return;
            int t = Cells[Pieces.Index(tr, tc)];
            if (t != 0 && Pieces.ColorOf(t) == color) return;
            if (capturesOnly && t == 0) return;
            moves.Add(new Move(from, Pieces.Index(tr, tc)));
        }

        switch (Pieces.TypeOf(p))
        {
            case Pieces.King:
                for (int d = 0; d < 4; d++)
                {
                    int tr = r + OrthoDr[d], tc = c + OrthoDc[d];
                    if (Pieces.InPalace(color, tr, tc)) TryAdd(tr, tc);
                }
                break;

            case Pieces.Advisor:
                for (int d = 0; d < 4; d++)
                {
                    int tr = r + DiagDr[d], tc = c + DiagDc[d];
                    if (Pieces.InPalace(color, tr, tc)) TryAdd(tr, tc);
                }
                break;

            case Pieces.Elephant:
                for (int d = 0; d < 4; d++)
                {
                    int tr = r + 2 * DiagDr[d], tc = c + 2 * DiagDc[d];
                    if (!Pieces.OnBoard(tr, tc) || !Pieces.OnOwnSide(color, tr)) continue;
                    if (Cells[Pieces.Index(r + DiagDr[d], c + DiagDc[d])] != 0) continue; // cản mắt tượng
                    TryAdd(tr, tc);
                }
                break;

            case Pieces.Horse:
                for (int d = 0; d < 8; d++)
                {
                    int tr = r + HorseDr[d], tc = c + HorseDc[d];
                    if (!Pieces.OnBoard(tr, tc)) continue;
                    if (Cells[Pieces.Index(r + HorseLegDr[d], c + HorseLegDc[d])] != 0) continue; // cản chân mã
                    TryAdd(tr, tc);
                }
                break;

            case Pieces.Rook:
                for (int d = 0; d < 4; d++)
                {
                    int tr = r + OrthoDr[d], tc = c + OrthoDc[d];
                    while (Pieces.OnBoard(tr, tc))
                    {
                        int t = Cells[Pieces.Index(tr, tc)];
                        if (t == 0) { if (!capturesOnly) moves.Add(new Move(from, Pieces.Index(tr, tc))); }
                        else { if (Pieces.ColorOf(t) != color) moves.Add(new Move(from, Pieces.Index(tr, tc))); break; }
                        tr += OrthoDr[d]; tc += OrthoDc[d];
                    }
                }
                break;

            case Pieces.Cannon:
                for (int d = 0; d < 4; d++)
                {
                    int tr = r + OrthoDr[d], tc = c + OrthoDc[d];
                    bool screen = false;
                    while (Pieces.OnBoard(tr, tc))
                    {
                        int t = Cells[Pieces.Index(tr, tc)];
                        if (!screen)
                        {
                            if (t == 0) { if (!capturesOnly) moves.Add(new Move(from, Pieces.Index(tr, tc))); }
                            else screen = true;
                        }
                        else if (t != 0)
                        {
                            if (Pieces.ColorOf(t) != color) moves.Add(new Move(from, Pieces.Index(tr, tc)));
                            break;
                        }
                        tr += OrthoDr[d]; tc += OrthoDc[d];
                    }
                }
                break;

            case Pieces.Pawn:
                {
                    int forward = color == Pieces.Red ? -1 : 1;
                    TryAdd(r + forward, c);
                    if (!Pieces.OnOwnSide(color, r)) // đã qua sông: được đi ngang
                    {
                        TryAdd(r, c - 1);
                        TryAdd(r, c + 1);
                    }
                }
                break;
        }
    }

    /// <summary>Danh sách nước đi hợp lệ hoàn toàn của bên đang tới lượt.</summary>
    public List<Move> GenerateLegalMoves()
    {
        var pseudo = new List<Move>(64);
        GeneratePseudoMoves(SideToMove, pseudo);
        var legal = new List<Move>(pseudo.Count);
        int me = SideToMove;
        foreach (var m in pseudo)
        {
            int cap = MakeMove(m);
            if (!IsInCheck(me)) legal.Add(m);
            UnmakeMove(m, cap);
        }
        return legal;
    }

    public bool HasLegalMove()
    {
        var pseudo = new List<Move>(64);
        GeneratePseudoMoves(SideToMove, pseudo);
        int me = SideToMove;
        foreach (var m in pseudo)
        {
            int cap = MakeMove(m);
            bool ok = !IsInCheck(me);
            UnmakeMove(m, cap);
            if (ok) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ kiểm tra chiếu

    public int FindKing(int color)
    {
        int king = color * Pieces.King;
        // Tướng chỉ nằm trong cung nên chỉ cần quét 9 ô
        int r0 = color == Pieces.Red ? 7 : 0;
        for (int r = r0; r < r0 + 3; r++)
            for (int c = 3; c <= 5; c++)
                if (Cells[Pieces.Index(r, c)] == king) return Pieces.Index(r, c);
        return -1;
    }

    /// <summary>Tướng của bên color có đang bị chiếu không (kể cả lộ mặt Tướng).</summary>
    public bool IsInCheck(int color)
    {
        int k = FindKing(color);
        if (k < 0) return true;
        int kr = Pieces.Row(k), kc = Pieces.Col(k);
        int enemy = -color;

        // Xe, Pháo, Tướng đối mặt theo 4 hướng
        for (int d = 0; d < 4; d++)
        {
            int tr = kr + OrthoDr[d], tc = kc + OrthoDc[d];
            int seen = 0;
            while (Pieces.OnBoard(tr, tc))
            {
                int t = Cells[Pieces.Index(tr, tc)];
                if (t != 0)
                {
                    seen++;
                    if (seen == 1)
                    {
                        if (t == enemy * Pieces.Rook) return true;
                        if (t == enemy * Pieces.King && OrthoDc[d] == 0) return true; // lộ mặt Tướng
                    }
                    else
                    {
                        if (t == enemy * Pieces.Cannon) return true;
                        break;
                    }
                }
                tr += OrthoDr[d]; tc += OrthoDc[d];
            }
        }

        // Mã: quân Mã ở ô H chiếu được Tướng nếu chân của Mã (tính từ H) trống
        for (int d = 0; d < 8; d++)
        {
            int hr = kr - HorseDr[d], hc = kc - HorseDc[d];
            if (!Pieces.OnBoard(hr, hc)) continue;
            if (Cells[Pieces.Index(hr, hc)] != enemy * Pieces.Horse) continue;
            if (Cells[Pieces.Index(hr + HorseLegDr[d], hc + HorseLegDc[d])] == 0) return true;
        }

        // Tốt
        int enemyPawn = enemy * Pieces.Pawn;
        int pawnRow = color == Pieces.Red ? kr - 1 : kr + 1; // Tốt địch đứng phía trước Tướng
        if (Pieces.OnBoard(pawnRow, kc) && Cells[Pieces.Index(pawnRow, kc)] == enemyPawn) return true;
        if (kc - 1 >= 0 && Cells[Pieces.Index(kr, kc - 1)] == enemyPawn) return true;
        if (kc + 1 < 9 && Cells[Pieces.Index(kr, kc + 1)] == enemyPawn) return true;

        return false;
    }

    private bool KingsFacing()
    {
        int rk = FindKing(Pieces.Red), bk = FindKing(Pieces.Black);
        if (rk < 0 || bk < 0 || Pieces.Col(rk) != Pieces.Col(bk)) return false;
        for (int i = bk + 9; i < rk; i += 9) if (Cells[i] != 0) return false;
        return true;
    }

    // ------------------------------------------------------------------ giải thích nước sai

    /// <summary>
    /// Kiểm tra một nước đi của bên tới lượt. Trả về null nếu hợp lệ, ngược lại trả về lý do (tiếng Việt).
    /// </summary>
    public string? ValidateMove(Move m)
    {
        if (m.From < 0 || m.From >= 90 || m.To < 0 || m.To >= 90) return "Toạ độ nằm ngoài bàn cờ.";
        int p = Cells[m.From];
        if (p == 0) return "Không có quân ở ô xuất phát.";
        if (Pieces.ColorOf(p) != SideToMove) return "Đó không phải quân của bạn.";
        if (m.From == m.To) return "Ô đích trùng ô xuất phát.";
        int t = Cells[m.To];
        if (t != 0 && Pieces.ColorOf(t) == SideToMove) return "Không thể ăn quân của mình.";

        var list = new List<Move>(17);
        GeneratePieceMoves(m.From, list);
        if (!list.Contains(m)) return ExplainShapeError(m);

        int me = SideToMove;
        bool wasInCheck = IsInCheck(me);
        int cap = MakeMove(m);
        bool facing = KingsFacing();
        bool inCheck = IsInCheck(me);
        UnmakeMove(m, cap);
        if (!inCheck) return null;
        if (facing) return "Hai Tướng không được đối mặt trực tiếp.";
        return wasInCheck ? "Tướng đang bị chiếu, phải đỡ chiếu trước." : "Nước này khiến Tướng của bạn bị chiếu.";
    }

    private string ExplainShapeError(Move m)
    {
        int p = Cells[m.From];
        int color = Pieces.ColorOf(p);
        int fr = Pieces.Row(m.From), fc = Pieces.Col(m.From);
        int tr = Pieces.Row(m.To), tc = Pieces.Col(m.To);
        int dr = tr - fr, dc = tc - fc;
        string name = Pieces.VietName(p);

        switch (Pieces.TypeOf(p))
        {
            case Pieces.King:
            case Pieces.Advisor:
                if (!Pieces.InPalace(color, tr, tc)) return $"{name} không được ra khỏi cung.";
                break;
            case Pieces.Elephant:
                if (Math.Abs(dr) == 2 && Math.Abs(dc) == 2)
                {
                    if (!Pieces.OnOwnSide(color, tr)) return "Tượng không được qua sông.";
                    return "Tượng bị cản mắt.";
                }
                break;
            case Pieces.Horse:
                if ((Math.Abs(dr) == 2 && Math.Abs(dc) == 1) || (Math.Abs(dr) == 1 && Math.Abs(dc) == 2))
                    return "Mã bị cản chân.";
                break;
            case Pieces.Rook:
                if (dr == 0 || dc == 0) return "Xe bị quân khác cản đường.";
                break;
            case Pieces.Cannon:
                if (dr == 0 || dc == 0)
                    return Cells[m.To] != 0 ? "Pháo cần đúng một ngòi để ăn quân." : "Pháo bị quân khác cản đường.";
                break;
            case Pieces.Pawn:
                {
                    int forward = color == Pieces.Red ? -1 : 1;
                    if (dr == -forward) return "Tốt không được đi lùi.";
                    if (dr == 0 && Math.Abs(dc) == 1 && Pieces.OnOwnSide(color, fr)) return "Tốt chưa qua sông không được đi ngang.";
                    break;
                }
        }
        return $"{name} không đi được như vậy.";
    }
}
