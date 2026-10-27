namespace CoTuongOnline.Engine;

/// <summary>
/// Ký hiệu nước đi kiểu Việt Nam: "Pháo 2 bình 5", "Mã 8 tiến 7", "Xe trước thoái 2".
/// Cột được đánh số 1–9 từ phải sang trái theo góc nhìn của bên đi quân.
/// Phải gọi TRƯỚC khi đi nước (dựa vào thế cờ trước nước đi).
/// </summary>
public static class Notation
{
    public static string Describe(Position before, Move m)
    {
        int p = before.Cells[m.From];
        int color = Pieces.ColorOf(p);
        int type = Pieces.TypeOf(p);
        int fr = Pieces.Row(m.From), fc = Pieces.Col(m.From);
        int tr = Pieces.Row(m.To), tc = Pieces.Col(m.To);

        string name = Pieces.VietName(p);
        string label = $"{name} {ColumnNumber(color, fc)}";

        // Hai (hoặc nhiều) quân cùng loại trên cùng một cột → dùng "trước / giữa / sau"
        var sameColumn = new List<int>();
        for (int r = 0; r < 10; r++)
            if (before.Cells[Pieces.Index(r, fc)] == p) sameColumn.Add(r);
        if (sameColumn.Count is 2 or 3 && type != Pieces.King)
        {
            // "trước" là quân tiến xa hơn về phía đối phương
            var ordered = color == Pieces.Red ? sameColumn.OrderBy(r => r).ToList() : sameColumn.OrderByDescending(r => r).ToList();
            int idx = ordered.IndexOf(fr);
            string pos = sameColumn.Count == 2
                ? (idx == 0 ? "trước" : "sau")
                : idx switch { 0 => "trước", 1 => "giữa", _ => "sau" };
            label = $"{name} {pos}";
        }

        if (tr == fr)
            return $"{label} bình {ColumnNumber(color, tc)}";

        bool forward = color == Pieces.Red ? tr < fr : tr > fr;
        string action = forward ? "tiến" : "thoái";
        bool straightMover = type is Pieces.Rook or Pieces.Cannon or Pieces.Pawn or Pieces.King;
        int value = straightMover ? Math.Abs(tr - fr) : ColumnNumber(color, tc);
        return $"{label} {action} {value}";
    }

    public static int ColumnNumber(int color, int col) => color == Pieces.Red ? 9 - col : col + 1;
}
