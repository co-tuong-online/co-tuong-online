namespace CoTuongOnline.Infrastructure;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    /// <summary>Khoá ký HS256, tối thiểu 32 ký tự. Môi trường thật: đặt qua biến môi trường Jwt__Key.</summary>
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "CoTuongOnline";
    public string Audience { get; set; } = "CoTuongOnline.Web";
    public int ExpireMinutes { get; set; } = 1440;
}

public sealed class EmailOptions
{
    public const string Section = "Email";
    /// <summary>
    /// true: không gửi email thật, mã OTP được ghi ra console và trả về trong response (devOtp)
    /// để demo/test không cần Gmail. false: gửi qua SMTP bằng MailKit.
    /// </summary>
    public bool DevMode { get; set; } = true;
    public string FromName { get; set; } = "Cờ Tướng Online";
    public string FromAddress { get; set; } = "";
    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    /// <summary>Gmail App Password — đặt qua biến môi trường Email__Smtp__Password, không commit.</summary>
    public string Password { get; set; } = "";
}

public sealed class SeedOptions
{
    public const string Section = "Seed";
    /// <summary>Tạo sẵn vài tài khoản demo khi khởi động (vì dữ liệu đang lưu trong RAM).</summary>
    public bool DemoUsers { get; set; } = true;
}
