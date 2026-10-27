using System.Net;
using System.Security.Cryptography;
using System.Text;
using CoTuongOnline.Common;
using CoTuongOnline.Dtos;
using CoTuongOnline.Infrastructure;
using CoTuongOnline.Models;
using CoTuongOnline.Repositories;
using CoTuongOnline.Services.Email;
using Microsoft.Extensions.Options;

namespace CoTuongOnline.Services.Auth;

public sealed class AuthService
{
    public const int OtpExpireMinutes = 5;
    public const int OtpResendCooldownSec = 60;
    public const int OtpMaxAttempts = 5;
    public static readonly TimeSpan UnverifiedNicknameHold = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan UnverifiedAccountLifetime = TimeSpan.FromHours(24);

    private readonly IUserRepository _users;
    private readonly IOtpRepository _otps;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly EmailOptions _emailOptions;
    private readonly LoginAttemptTracker _attempts;

    public AuthService(IUserRepository users, IOtpRepository otps, IPasswordHasher hasher, ITokenService tokens,
        IEmailSender email, IOptions<EmailOptions> emailOptions, LoginAttemptTracker attempts)
    {
        _attempts = attempts;
        _users = users;
        _otps = otps;
        _hasher = hasher;
        _tokens = tokens;
        _email = email;
        _emailOptions = emailOptions.Value;
    }

    // ------------------------------------------------------------------ đăng ký

    public async Task<OtpSentResponse> RegisterAsync(RegisterRequest req)
    {
        string email = NormalizeEmail(req.Email);
        string nickname = req.Nickname.Trim();
        EnsureStrongPassword(req.Password);

        var existing = await _users.GetByEmailAsync(email);
        if (existing is { IsVerified: true })
            throw AppException.Conflict(ErrorCodes.EmailExists, "Email này đã được đăng ký. Hãy đăng nhập hoặc dùng chức năng quên mật khẩu.");

        var sameNick = await _users.GetByNicknameAsync(nickname);
        if (sameNick is not null && sameNick.Id != existing?.Id)
        {
            // Tài khoản chưa xác nhận OTP quá 15 phút (ví dụ đăng ký bằng email không tồn tại) không được giữ biệt danh
            bool abandoned = !sameNick.IsVerified && sameNick.CreatedAt < DateTime.UtcNow - UnverifiedNicknameHold;
            if (!abandoned)
                throw AppException.Conflict(ErrorCodes.NicknameExists, "Biệt danh đã có người dùng, hãy chọn biệt danh khác.");
            await _users.DeleteAsync(sameNick);
        }

        if (existing is null)
        {
            existing = await _users.AddAsync(new User
            {
                Email = email,
                Nickname = nickname,
                PasswordHash = _hasher.Hash(req.Password),
                IsVerified = false
            });
        }
        else
        {
            // Đăng ký lại khi chưa xác nhận OTP: cập nhật thông tin mới
            existing.Nickname = nickname;
            existing.PasswordHash = _hasher.Hash(req.Password);
            await _users.UpdateAsync(existing);
        }

        return await IssueOtpAsync(existing, OtpPurposes.Register);
    }

    public async Task<AuthResponse> VerifyRegisterOtpAsync(VerifyOtpRequest req)
    {
        string email = NormalizeEmail(req.Email);
        var user = await _users.GetByEmailAsync(email)
                   ?? throw AppException.NotFound(ErrorCodes.AccountNotFound, "Không tìm thấy tài khoản với email này.");
        if (user.IsVerified)
            throw AppException.Conflict(ErrorCodes.EmailExists, "Tài khoản đã được xác nhận, hãy đăng nhập.");

        await ConsumeOtpAsync(email, OtpPurposes.Register, req.Otp);
        user.IsVerified = true;
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        return BuildAuthResponse(user);
    }

    public async Task<OtpSentResponse> ResendOtpAsync(ResendOtpRequest req)
    {
        string email = NormalizeEmail(req.Email);
        string purpose = req.Purpose == OtpPurposes.ResetPassword ? OtpPurposes.ResetPassword : OtpPurposes.Register;
        var user = await _users.GetByEmailAsync(email)
                   ?? throw AppException.NotFound(ErrorCodes.AccountNotFound, "Không tìm thấy tài khoản với email này.");
        if (purpose == OtpPurposes.Register && user.IsVerified)
            throw AppException.Conflict(ErrorCodes.EmailExists, "Tài khoản đã được xác nhận, hãy đăng nhập.");
        return await IssueOtpAsync(user, purpose);
    }

    // ------------------------------------------------------------------ đăng nhập

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        string login = req.Login.Trim();
        var user = login.Contains('@')
            ? await _users.GetByEmailAsync(NormalizeEmail(login))
            : await _users.GetByNicknameAsync(login);

        // Đếm sai theo tài khoản (đăng nhập bằng email hay biệt danh đều tính chung một bộ đếm)
        string attemptKey = user is not null ? $"user:{user.Id}" : $"login:{login.ToLowerInvariant()}";
        if (_attempts.LockedFor(attemptKey) is TimeSpan wait)
            throw new AppException(ErrorCodes.AccountLocked,
                $"Đăng nhập sai quá {LoginAttemptTracker.MaxFailures} lần. Vui lòng thử lại sau {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} phút.",
                HttpStatusCode.TooManyRequests);

        if (user is null || !_hasher.Verify(req.Password, user.PasswordHash))
        {
            _attempts.RecordFailure(attemptKey);
            throw new AppException(ErrorCodes.InvalidCredentials, "Sai email/biệt danh hoặc mật khẩu.", HttpStatusCode.Unauthorized);
        }
        if (!user.IsVerified)
            throw new AppException(ErrorCodes.NotVerified, "Tài khoản chưa xác nhận email. Hãy nhập mã OTP để kích hoạt.", HttpStatusCode.Forbidden);

        _attempts.Reset(attemptKey);
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        return BuildAuthResponse(user);
    }

    public async Task<UserDto> GetMeAsync(int userId)
    {
        var user = await _users.GetByIdAsync(userId)
                   ?? throw new AppException(ErrorCodes.Unauthorized, "Tài khoản không còn tồn tại.", HttpStatusCode.Unauthorized);
        return ToDto(user);
    }

    // ------------------------------------------------------------------ quên mật khẩu

    public async Task<OtpSentResponse> ForgotPasswordAsync(ForgotPasswordRequest req)
    {
        string email = NormalizeEmail(req.Email);
        var user = await _users.GetByEmailAsync(email);
        if (user is null || !user.IsVerified)
            throw AppException.NotFound(ErrorCodes.AccountNotFound, "Không tìm thấy tài khoản đã kích hoạt với email này.");
        return await IssueOtpAsync(user, OtpPurposes.ResetPassword);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest req)
    {
        string email = NormalizeEmail(req.Email);
        EnsureStrongPassword(req.NewPassword);
        var user = await _users.GetByEmailAsync(email)
                   ?? throw AppException.NotFound(ErrorCodes.AccountNotFound, "Không tìm thấy tài khoản với email này.");
        await ConsumeOtpAsync(email, OtpPurposes.ResetPassword, req.Otp);
        user.PasswordHash = _hasher.Hash(req.NewPassword);
        await _users.UpdateAsync(user);
    }

    // ------------------------------------------------------------------ OTP

    private async Task<OtpSentResponse> IssueOtpAsync(User user, string purpose)
    {
        var last = await _otps.GetLatestAsync(user.Email, purpose);
        if (last is not null && !last.Used)
        {
            var wait = OtpResendCooldownSec - (int)(DateTime.UtcNow - last.CreatedAt).TotalSeconds;
            if (wait > 0)
                throw new AppException(ErrorCodes.OtpCooldown, $"Vui lòng chờ {wait} giây trước khi gửi lại mã.", (HttpStatusCode)429);
        }

        string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await _otps.AddAsync(new OtpCode
        {
            Email = user.Email,
            Purpose = purpose,
            CodeHash = HashOtp(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpExpireMinutes)
        });
        await _email.SendOtpAsync(user.Email, user.Nickname, code, purpose, OtpExpireMinutes);

        return new OtpSentResponse(user.Email, OtpExpireMinutes * 60, OtpResendCooldownSec,
            _emailOptions.DevMode ? code : null);
    }

    private async Task ConsumeOtpAsync(string email, string purpose, string code)
    {
        var otp = await _otps.GetLatestAsync(email, purpose);
        if (otp is null || otp.Used)
            throw new AppException(ErrorCodes.OtpInvalid, "Mã OTP không đúng hoặc đã được dùng. Hãy gửi lại mã mới.");
        if (otp.ExpiresAt < DateTime.UtcNow)
            throw new AppException(ErrorCodes.OtpExpired, "Mã OTP đã hết hạn. Hãy gửi lại mã mới.");
        if (otp.Attempts >= OtpMaxAttempts)
            throw new AppException(ErrorCodes.OtpTooManyAttempts, "Nhập sai quá nhiều lần. Hãy gửi lại mã mới.");

        bool ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(HashOtp(code.Trim())), Encoding.UTF8.GetBytes(otp.CodeHash));
        if (!ok)
        {
            otp.Attempts++;
            await _otps.UpdateAsync(otp);
            int left = OtpMaxAttempts - otp.Attempts;
            throw new AppException(ErrorCodes.OtpInvalid,
                left > 0 ? $"Mã OTP không đúng, còn {left} lần thử." : "Nhập sai quá nhiều lần. Hãy gửi lại mã mới.");
        }
        otp.Used = true;
        await _otps.UpdateAsync(otp);
    }

    private static string HashOtp(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    // ------------------------------------------------------------------ tiện ích

    private AuthResponse BuildAuthResponse(User user)
    {
        var (token, expires) = _tokens.CreateToken(user);
        return new AuthResponse(token, expires, ToDto(user));
    }

    public static UserDto ToDto(User u) =>
        new(u.Id, u.Email, u.Nickname, u.GamesPlayed, u.Wins, u.Losses, u.Draws, u.CreatedAt);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static void EnsureStrongPassword(string password)
    {
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new AppException(ErrorCodes.WeakPassword, "Mật khẩu phải có cả chữ và số.");
    }
}
