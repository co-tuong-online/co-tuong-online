namespace CoTuongOnline.Models;

// Các entity dưới đây tương ứng 1-1 với bảng SQL Server sẽ thêm sau (EF Core Code First).
// Hiện tại chúng được lưu trong bộ nhớ (Repositories/InMemory). Khi có DB chỉ cần
// viết repository EF Core và đổi đăng ký DI, service/controller giữ nguyên.

/// <summary>Bảng users.</summary>
public sealed class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsVerified { get; set; }
    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public static class OtpPurposes
{
    public const string Register = "REGISTER";
    public const string ResetPassword = "RESET_PASSWORD";
}

/// <summary>Bảng otp_codes. Chỉ lưu mã đã băm SHA-256, không lưu mã gốc.</summary>
public sealed class OtpCode
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Purpose { get; set; } = OtpPurposes.Register;
    public string CodeHash { get; set; } = "";
    public int Attempts { get; set; }
    public bool Used { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Bảng games: cả ván online (room_id có giá trị) lẫn ván với máy.</summary>
public sealed class GameRecord
{
    public long Id { get; set; }
    public string? RoomCode { get; set; }
    public int? RedPlayerId { get; set; }
    public int? BlackPlayerId { get; set; }
    public string OpponentType { get; set; } = "HUMAN"; // HUMAN | AI
    public string? AiLevel { get; set; }
    public string Status { get; set; } = "PLAYING";      // PLAYING | FINISHED | ABORTED
    public string? Result { get; set; }                  // RED_WIN | BLACK_WIN | DRAW
    public string? EndReason { get; set; }
    public string CurrentStateJson { get; set; } = "{}";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
}

/// <summary>Bảng game_moves.</summary>
public sealed class GameMoveRecord
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public int Ply { get; set; }
    public int FromRow { get; set; }
    public int FromCol { get; set; }
    public int ToRow { get; set; }
    public int ToCol { get; set; }
    public string Piece { get; set; } = "";
    public string? Captured { get; set; }
    public string Notation { get; set; } = "";
    public long SpentMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Bảng saved_games: ván với máy đã lưu (phạm vi rút gọn chỉ lưu ván với máy).</summary>
public sealed class SavedGame
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string OpponentType { get; set; } = "AI";
    public string AiLevel { get; set; } = "EASY";
    public string PlayerColor { get; set; } = "red";
    public string StateJson { get; set; } = "{}";
    public int MoveCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
