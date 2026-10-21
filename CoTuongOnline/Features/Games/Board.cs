using System.Text.Json;

namespace CoTuongOnline.Features.Games;

public sealed record Piece(string Id, string Type, string Color, string File, int Row, int Col);
public sealed record Board(List<Piece> Pieces, string Turn = "do", string Status = "active", string? Winner = null, int SchemaVersion = 1)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string Serialize() => JsonSerializer.Serialize(this, Json);
    public static Board Read(string json) => JsonSerializer.Deserialize<Board>(json, Json)!;
    public static Board Initial()
    {
        using var stream = typeof(Board).Assembly.GetManifestResourceStream("CoTuongOnline.Features.Games.initial.json")!;
        using var document = JsonDocument.Parse(stream);
        return new(document.RootElement.GetProperty("pieces").Deserialize<List<Piece>>(Json)!);
    }
}

public interface IMovementEngine
{
    Board Move(Board board, string color, string pieceId, int row, int col);
}

// This milestone enforces piece geometry and turns, but deliberately does not evaluate check.
public sealed class MovementEngine : IMovementEngine
{
    public Board Move(Board board, string color, string pieceId, int row, int col)
    {
        if (board.Status != "active") throw new InvalidOperationException("Ván cờ đã kết thúc.");
        if (board.Turn != color) throw new InvalidOperationException("Chưa đến lượt của bạn.");
        if (row is < 0 or > 9 || col is < 0 or > 8) throw new InvalidOperationException("Vị trí ngoài bàn cờ.");
        var piece = board.Pieces.SingleOrDefault(p => p.Id == pieceId);
        if (piece is null || piece.Color != color) throw new InvalidOperationException("Bạn không thể đi quân này.");
        var target = board.Pieces.SingleOrDefault(p => p.Row == row && p.Col == col);
        if (target?.Color == color) throw new InvalidOperationException("Vị trí có quân cùng màu.");
        int dr = row - piece.Row, dc = col - piece.Col, ar = Math.Abs(dr), ac = Math.Abs(dc);
        bool At(int r, int c) => board.Pieces.Any(p => p.Row == r && p.Col == c);
        bool Palace() => col is >= 3 and <= 5 && (color == "do" ? row is >= 7 and <= 9 : row is >= 0 and <= 2);
        int Between() => board.Pieces.Count(p => dr == 0
            ? p.Row == row && p.Col > Math.Min(piece.Col, col) && p.Col < Math.Max(piece.Col, col)
            : p.Col == col && p.Row > Math.Min(piece.Row, row) && p.Row < Math.Max(piece.Row, row));
        bool straight = (dr == 0) != (dc == 0);
        bool valid = piece.Type switch
        {
            "xe" => straight && Between() == 0,
            "phao" => straight && Between() == (target is null ? 0 : 1),
            "ma" => ar == 2 && ac == 1 && !At(piece.Row + Math.Sign(dr), piece.Col)
                 || ar == 1 && ac == 2 && !At(piece.Row, piece.Col + Math.Sign(dc)),
            "tuong" => ar == 2 && ac == 2 && !At(piece.Row + dr / 2, piece.Col + dc / 2)
                      && (color == "do" ? row >= 5 : row <= 4),
            "si" => ar == 1 && ac == 1 && Palace(),
            "chu_tuong" => ar + ac == 1 && Palace(),
            "tot" => dc == 0 && dr == (color == "do" ? -1 : 1)
                  || dr == 0 && ac == 1 && (color == "do" ? piece.Row <= 4 : piece.Row >= 5),
            _ => false
        };
        if (!valid) throw new InvalidOperationException("Nước đi không hợp lệ.");
        var pieces = board.Pieces.Where(p => p.Id != pieceId && p.Id != target?.Id).ToList();
        pieces.Add(piece with { Row = row, Col = col });
        bool won = target?.Type == "chu_tuong";
        return new(pieces, color == "do" ? "den" : "do", won ? "finished" : "active", won ? color : null);
    }
}
