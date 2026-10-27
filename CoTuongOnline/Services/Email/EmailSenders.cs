using CoTuongOnline.Infrastructure;
using CoTuongOnline.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CoTuongOnline.Services.Email;

public interface IEmailSender
{
    Task SendOtpAsync(string toEmail, string nickname, string code, string purpose, int expiresInMin);
}

/// <summary>Chế độ dev: không gửi email, chỉ ghi mã OTP ra console của server.</summary>
public sealed class DevEmailSender : IEmailSender
{
    private readonly ILogger<DevEmailSender> _logger;
    public DevEmailSender(ILogger<DevEmailSender> logger) => _logger = logger;

    public Task SendOtpAsync(string toEmail, string nickname, string code, string purpose, int expiresInMin)
    {
        _logger.LogWarning("[DEV EMAIL] OTP {Purpose} cho {Email} ({Nickname}): {Code} (hết hạn sau {Min} phút)",
            purpose, toEmail, nickname, code, expiresInMin);
        return Task.CompletedTask;
    }
}

/// <summary>Gửi OTP qua SMTP Gmail (cổng 587, STARTTLS) bằng MailKit.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendOtpAsync(string toEmail, string nickname, string code, string purpose, int expiresInMin)
    {
        string subject = purpose == OtpPurposes.Register
            ? "Mã xác nhận đăng ký Cờ Tướng Online"
            : "Mã đặt lại mật khẩu Cờ Tướng Online";
        string action = purpose == OtpPurposes.Register ? "hoàn tất đăng ký" : "đặt lại mật khẩu";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(new MailboxAddress(nickname, toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder
        {
            HtmlBody = $"""
                <div style="font-family:Segoe UI,Arial,sans-serif;max-width:480px;margin:auto;padding:24px;border:1px solid #e5d3a8;border-radius:12px">
                  <h2 style="color:#9e2a1f;margin:0 0 12px">Cờ Tướng Online</h2>
                  <p>Chào <b>{System.Net.WebUtility.HtmlEncode(nickname)}</b>,</p>
                  <p>Mã OTP để {action} của bạn là:</p>
                  <p style="font-size:32px;letter-spacing:8px;font-weight:700;color:#1f3a33;margin:16px 0">{code}</p>
                  <p>Mã có hiệu lực trong {expiresInMin} phút. Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>
                </div>
                """,
            TextBody = $"Mã OTP để {action}: {code} (hiệu lực {expiresInMin} phút)."
        }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Smtp.Host, _options.Smtp.Port, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(_options.Smtp.Username, _options.Smtp.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
        _logger.LogInformation("Đã gửi OTP {Purpose} tới {Email}", purpose, toEmail);
    }
}
