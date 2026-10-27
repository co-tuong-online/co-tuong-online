namespace CoTuongOnline.Engine;

/// <summary>
/// Trạng thái bàn cờ dạng JSON (lưu vào games.current_state và saved_games.state_json).
/// board[row][col]: row 0 = phía Đen (trên), row 9 = phía Đỏ (dưới). Ô trống là null.
/// </summary>
public sealed class BoardState
{
    public int SchemaVersion { get; set; } = 1;
    public string?[][] Board { get; set; } = Array.Empty<string?[]>();
    public string SideToMove { get; set; } = "red";
    public int Ply { get; set; }
    public int PliesSinceCapture { get; set; }
    public ClockState? Clock { get; set; }
    public List<MoveRecord> Moves { get; set; } = new();
}

public sealed class ClockState
{
    public long RedMs { get; set; }
    public long BlackMs { get; set; }
    public int MoveTimeSec { get; set; }
    /// <summary>Thời gian đã trôi qua của nước hiện tại (chỉ dùng khi gửi cho client).</summary>
    public long TurnElapsedMs { get; set; }
}

public sealed class MoveRecord
{
    public int Ply { get; set; }
    public Coord From { get; set; } = new(0, 0);
    public Coord To { get; set; } = new(0, 0);
    public string Piece { get; set; } = "";
    public string? Captured { get; set; }
    public string Notation { get; set; } = "";
    public long SpentMs { get; set; }
}

/// <summary>Nước đi hợp lệ gửi cho client để tô sáng ô có thể đi.</summary>
public sealed record LegalMove(Coord From, Coord To);
