using System.Net;

namespace CoTuongOnline.Common;

/// <summary>Mọi response REST đều có dạng { success, code, message, data }.</summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public string Code { get; init; } = "OK";
    public string? Message { get; init; }
    public T? Data { get; init; }
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null) =>
        new() { Success = true, Code = "OK", Message = message, Data = data };

    public static ApiResponse<object> Ok(string? message = null) =>
        new() { Success = true, Code = "OK", Message = message };

    public static ApiResponse<object> Fail(string code, string message) =>
        new() { Success = false, Code = code, Message = message };
}

/// <summary>Lỗi nghiệp vụ: service ném ra, middleware chuyển thành response JSON với mã lỗi.</summary>
public sealed class AppException : Exception
{
    public string Code { get; }
    public HttpStatusCode Status { get; }

    public AppException(string code, string message, HttpStatusCode status = HttpStatusCode.BadRequest) : base(message)
    {
        Code = code;
        Status = status;
    }

    public static AppException NotFound(string code, string message) => new(code, message, HttpStatusCode.NotFound);
    public static AppException Forbidden(string code, string message) => new(code, message, HttpStatusCode.Forbidden);
    public static AppException Conflict(string code, string message) => new(code, message, HttpStatusCode.Conflict);
}

/// <summary>Danh mục mã lỗi dùng chung cho REST và SignalR (event ERROR).</summary>
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Internal = "INTERNAL_ERROR";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";

    public const string EmailExists = "AUTH_EMAIL_EXISTS";
    public const string NicknameExists = "AUTH_NICKNAME_EXISTS";
    public const string InvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string NotVerified = "AUTH_NOT_VERIFIED";
    public const string AccountNotFound = "AUTH_ACCOUNT_NOT_FOUND";
    public const string OtpInvalid = "AUTH_OTP_INVALID";
    public const string OtpExpired = "AUTH_OTP_EXPIRED";
    public const string OtpTooManyAttempts = "AUTH_OTP_TOO_MANY_ATTEMPTS";
    public const string OtpCooldown = "AUTH_OTP_COOLDOWN";
    public const string WeakPassword = "AUTH_WEAK_PASSWORD";
    public const string AccountLocked = "AUTH_ACCOUNT_LOCKED";

    public const string RoomNotFound = "ROOM_NOT_FOUND";
    public const string RoomFull = "ROOM_FULL";
    public const string RoomPlaying = "ROOM_PLAYING";
    public const string RoomNotOwner = "ROOM_NOT_OWNER";
    public const string RoomNotMember = "ROOM_NOT_MEMBER";
    public const string RoomGuestNotReady = "ROOM_GUEST_NOT_READY";
    public const string RoomNoGuest = "ROOM_NO_GUEST";
    public const string AlreadyInGame = "ROOM_ALREADY_IN_GAME";

    public const string InviteNotFound = "INVITE_NOT_FOUND";
    public const string InviteInvalid = "INVITE_INVALID";
    public const string UserOffline = "USER_OFFLINE";

    public const string GameNotFound = "GAME_NOT_FOUND";
    public const string GameNotYourTurn = "GAME_NOT_YOUR_TURN";
    public const string GameIllegalMove = "GAME_ILLEGAL_MOVE";
    public const string GameFinished = "GAME_FINISHED";
    public const string GameNotPlayer = "GAME_NOT_PLAYER";
    public const string UndoNotAllowed = "GAME_UNDO_NOT_ALLOWED";

    public const string SaveNotFound = "SAVE_NOT_FOUND";
    public const string SaveLimit = "SAVE_LIMIT_REACHED";

    public const string ChatInvalid = "CHAT_INVALID";
    public const string ChatTooFast = "CHAT_TOO_FAST";
}
