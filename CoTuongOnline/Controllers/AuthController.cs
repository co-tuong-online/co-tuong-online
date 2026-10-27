using CoTuongOnline.Common;
using CoTuongOnline.Dtos;
using CoTuongOnline.Services.Auth;
using CoTuongOnline.Services.Games;
using Microsoft.AspNetCore.Authorization;
using CoTuongOnline.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CoTuongOnline.Controllers;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(RateLimitSetup.AuthPolicy)]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    /// <summary>Đăng ký: tạo tài khoản chưa kích hoạt và gửi OTP về email.</summary>
    [HttpPost("register")]
    public async Task<ApiResponse<OtpSentResponse>> Register(RegisterRequest req) =>
        ApiResponse.Ok(await _auth.RegisterAsync(req), "Đã gửi mã OTP về email của bạn.");

    /// <summary>Xác nhận OTP đăng ký → kích hoạt tài khoản và đăng nhập luôn.</summary>
    [HttpPost("verify-otp")]
    public async Task<ApiResponse<AuthResponse>> VerifyOtp(VerifyOtpRequest req) =>
        ApiResponse.Ok(await _auth.VerifyRegisterOtpAsync(req), "Kích hoạt tài khoản thành công.");

    [HttpPost("resend-otp")]
    public async Task<ApiResponse<OtpSentResponse>> ResendOtp(ResendOtpRequest req) =>
        ApiResponse.Ok(await _auth.ResendOtpAsync(req), "Đã gửi lại mã OTP.");

    [HttpPost("login")]
    public async Task<ApiResponse<AuthResponse>> Login(LoginRequest req) =>
        ApiResponse.Ok(await _auth.LoginAsync(req), "Đăng nhập thành công.");

    [HttpPost("forgot-password")]
    public async Task<ApiResponse<OtpSentResponse>> ForgotPassword(ForgotPasswordRequest req) =>
        ApiResponse.Ok(await _auth.ForgotPasswordAsync(req), "Đã gửi mã OTP đặt lại mật khẩu.");

    [HttpPost("reset-password")]
    public async Task<ApiResponse<object>> ResetPassword(ResetPasswordRequest req)
    {
        await _auth.ResetPasswordAsync(req);
        return ApiResponse.Ok("Đổi mật khẩu thành công, hãy đăng nhập lại.");
    }

    [Authorize]
    [DisableRateLimiting] // gọi mỗi lần mở sảnh, không cần giới hạn
    [HttpGet("me")]
    public async Task<ApiResponse<UserDto>> Me() => ApiResponse.Ok(await _auth.GetMeAsync(User.GetUserId()));
}

[ApiController]
[Authorize]
[Route("api/v1/users")]
public sealed class UsersController : ControllerBase
{
    private readonly UserService _users;
    public UsersController(UserService users) => _users = users;

    /// <summary>Tìm người chơi theo biệt danh để mời vào phòng.</summary>
    [HttpGet("search")]
    public async Task<ApiResponse<List<UserSearchItem>>> Search([FromQuery] string? q) =>
        ApiResponse.Ok(await _users.SearchAsync(User.GetUserId(), q));

    [HttpGet("online")]
    public ApiResponse<object> Online() =>
        ApiResponse.Ok<object>(new { count = _users.OnlineCount, users = _users.Online(User.GetUserId()) });
}
