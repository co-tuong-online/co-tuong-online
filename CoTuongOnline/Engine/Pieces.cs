namespace CoTuongOnline.Engine;

/// <summary>
/// Quy ước toạ độ: bàn 10 hàng x 9 cột. row 0 là hàng cuối phía Đen (trên cùng),
/// row 9 là hàng cuối phía Đỏ (dưới cùng). col 0 là cột trái nhìn từ phía Đỏ.
/// Bên trong engine, ô (row, col) được đánh chỉ số index = row * 9 + col (0..89).
/// Quân được mã hoá bằng số nguyên: dương là Đỏ, âm là Đen, giá trị tuyệt đối là loại quân.
/// </summary>
public static class Pieces
{
    public const int Empty = 0;
    public const int King = 1;      // Tướng  (K)
    public const int Advisor = 2;   // Sĩ     (A)
    public const int Elephant = 3;  // Tượng  (B)
    public const int Horse = 4;     // Mã     (N)
    public const int Rook = 5;      // Xe     (R)
    public const int Cannon = 6;    // Pháo   (C)
    public const int Pawn = 7;      // Tốt    (P)

    public const int Red = 1;
    public const int Black = -1;

    private const string TypeLetters = " KABNRCP";

    public static int TypeOf(int piece) => Math.Abs(piece);
    public static int ColorOf(int piece) => piece > 0 ? Red : piece < 0 ? Black : 0;

    /// <summary>"rK" → 1, "bC" → -6, null → 0.</summary>
    public static int FromCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return Empty;
        if (code.Length != 2) throw new FormatException($"Mã quân không hợp lệ: {code}");
        int color = code[0] switch
        {
            'r' => Red,
            'b' => Black,
            _ => throw new FormatException($"Mã màu không hợp lệ: {code}")
        };
        int type = TypeLetters.IndexOf(code[1]);
        if (type <= 0) throw new FormatException($"Mã loại quân không hợp lệ: {code}");
        return color * type;
    }

    public static string? ToCode(int piece)
    {
        if (piece == Empty) return null;
        return $"{(piece > 0 ? 'r' : 'b')}{TypeLetters[TypeOf(piece)]}";
    }

    public static string VietName(int piece) => TypeOf(piece) switch
    {
        King => "Tướng",
        Advisor => "Sĩ",
        Elephant => "Tượng",
        Horse => "Mã",
        Rook => "Xe",
        Cannon => "Pháo",
        Pawn => "Tốt",
        _ => "?"
    };

    public static string ColorName(int color) => color == Red ? "red" : "black";
    public static int ParseColor(string? side) => side?.ToLowerInvariant() == "black" ? Black : Red;

    public static int Index(int row, int col) => row * 9 + col;
    public static int Row(int index) => index / 9;
    public static int Col(int index) => index % 9;
    public static bool OnBoard(int row, int col) => row >= 0 && row < 10 && col >= 0 && col < 9;

    /// <summary>Ô (row, col) có nằm trong cung của bên color hay không.</summary>
    public static bool InPalace(int color, int row, int col)
    {
        if (col < 3 || col > 5) return false;
        return color == Red ? row >= 7 && row <= 9 : row >= 0 && row <= 2;
    }

    /// <summary>Ô có nằm ở phần sân nhà (chưa qua sông) của bên color hay không.</summary>
    public static bool OnOwnSide(int color, int row) => color == Red ? row >= 5 : row <= 4;
}

/// <summary>Toạ độ dùng trong API và JSON: { "row": 7, "col": 1 }.</summary>
public sealed record Coord(int Row, int Col)
{
    public bool IsValid => Pieces.OnBoard(Row, Col);
    public int ToIndex() => Pieces.Index(Row, Col);
    public static Coord FromIndex(int index) => new(Pieces.Row(index), Pieces.Col(index));
}

/// <summary>Nước đi nội bộ của engine (theo chỉ số ô).</summary>
public readonly record struct Move(int From, int To)
{
    public static readonly Move None = new(-1, -1);
    public bool IsNone => From < 0;
}
