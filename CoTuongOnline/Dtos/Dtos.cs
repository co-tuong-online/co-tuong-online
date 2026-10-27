using System.ComponentModel.DataAnnotations;
using CoTuongOnline.Engine;

namespace CoTuongOnline.Dtos;

// ============================================================ Auth

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập biệt danh.")]
    [RegularExpression("^[a-zA-Z0-9_]{3,20}$", ErrorMessage = "Biệt danh gồm 3–20 ký tự: chữ không dấu, số hoặc dấu gạch dưới.")]
    public string Nickname { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(64, MinimumLength = 8, ErrorMessage = "Mật khẩu dài 8–64 ký tự.")]
    public string Password { get; set; } = "";
}

public sealed class VerifyOtpRequest
{
    [Required, EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập mã OTP.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã OTP gồm 6 chữ số.")]
    public string Otp { get; set; } = "";
}

public sealed class ResendOtpRequest
{
    [Required, EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = "";

    /// <summary>REGISTER hoặc RESET_PASSWORD.</summary>
    [Required]
    public string Purpose { get; set; } = "REGISTER";
}

public sealed class LoginRequest
{
    /// <summary>Email hoặc biệt danh.</summary>
    [Required(ErrorMessage = "Vui lòng nhập email hoặc biệt danh.")]
    public string Login { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    public string Password { get; set; } = "";
}

public sealed class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email."), EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = "";
}

public sealed class ResetPasswordRequest
{
    [Required, EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập mã OTP.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã OTP gồm 6 chữ số.")]
    public string Otp { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    [StringLength(64, MinimumLength = 8, ErrorMessage = "Mật khẩu dài 8–64 ký tự.")]
    public string NewPassword { get; set; } = "";
}

public sealed record OtpSentResponse(string Email, int ExpiresInSec, int ResendAfterSec, string? DevOtp);

public sealed record UserDto(int Id, string Email, string Nickname, int GamesPlayed, int Wins, int Losses, int Draws, DateTime CreatedAt);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);

public sealed record UserSearchItem(int Id, string Nickname, bool Online, string? RoomCode);

// ============================================================ Rooms

public sealed class RoomConfigDto
{
    /// <summary>Màu quân của chủ phòng: RED | BLACK | RANDOM.</summary>
    public string OwnerColor { get; set; } = "RANDOM";

    [Range(1, 60, ErrorMessage = "Tổng thời gian từ 1 đến 60 phút.")]
    public int TotalTimeMin { get; set; } = 10;

    [Range(10, 300, ErrorMessage = "Thời gian mỗi nước từ 10 đến 300 giây.")]
    public int MoveTimeSec { get; set; } = 60;

    /// <summary>Phòng riêng: không hiện ở danh sách phòng của sảnh, chỉ vào bằng mã/link/lời mời.</summary>
    public bool IsPrivate { get; set; }

    public RoomConfigDto Clone() => new()
    {
        OwnerColor = OwnerColor, TotalTimeMin = TotalTimeMin, MoveTimeSec = MoveTimeSec, IsPrivate = IsPrivate
    };
}

public sealed class CreateRoomRequest
{
    public RoomConfigDto? Config { get; set; }
}

public sealed class ReadyRequest
{
    public bool Ready { get; set; }
}

public sealed record PlayerDto(int UserId, string Nickname, bool Online);

public sealed record RoomDto(
    string Code,
    string Status,
    int OwnerId,
    PlayerDto Owner,
    PlayerDto? Guest,
    bool GuestReady,
    RoomConfigDto Config,
    long? GameId,
    DateTime CreatedAt);

public sealed record RoomListItem(string Code, string OwnerNickname, RoomConfigDto Config, DateTime CreatedAt);

public sealed record ChatMessageDto(long Id, string Code, int UserId, string Nickname, string Content, DateTime SentAt);

// ============================================================ Games

public sealed class MoveRequest
{
    [Required] public Coord From { get; set; } = new(0, 0);
    [Required] public Coord To { get; set; } = new(0, 0);
}

public sealed record GameStateDto(
    long GameId,
    string Mode,
    string? RoomCode,
    string Status,
    string? Result,
    string? EndReason,
    PlayerDto Red,
    PlayerDto Black,
    string? MyColor,
    BoardState State,
    List<LegalMove> LegalMoves,
    bool InCheck,
    string? OfflineNickname,
    DateTime? ForfeitAt);

public sealed class CreateAiGameRequest
{
    /// <summary>EASY | HARD.</summary>
    [Required] public string Level { get; set; } = AiLevels.Easy;

    /// <summary>red | black | random.</summary>
    public string PlayerColor { get; set; } = "red";
}

public sealed record AiGameDto(
    long GameId,
    string Level,
    string PlayerColor,
    string Status,
    string? Result,
    string? EndReason,
    BoardState State,
    List<LegalMove> LegalMoves,
    bool InCheck,
    bool CanUndo,
    int UndoLeft,
    long? SavedGameId,
    string? SavedGameName);

public sealed record AiMoveResult(MoveRecord? Move, AiGameDto Game);

public sealed class SaveGameRequest
{
    [Required] public long GameId { get; set; }

    [Required(ErrorMessage = "Vui lòng đặt tên cho ván lưu.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Tên ván lưu dài 1–50 ký tự.")]
    public string Name { get; set; } = "";

    /// <summary>Nếu có: ghi đè lên bản lưu này thay vì tạo bản mới.</summary>
    public long? OverwriteId { get; set; }
}

public sealed class RenameSavedGameRequest
{
    [Required(ErrorMessage = "Vui lòng nhập tên.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Tên ván lưu dài 1–50 ký tự.")]
    public string Name { get; set; } = "";
}

public sealed record SavedGameDto(
    long Id,
    string Name,
    string AiLevel,
    string PlayerColor,
    int MoveCount,
    string SideToMove,
    string?[][] Board,
    DateTime CreatedAt,
    DateTime UpdatedAt);
