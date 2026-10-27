using System.Net;
using System.Threading.RateLimiting;
using CoTuongOnline.Common;
using Microsoft.AspNetCore.RateLimiting;

namespace CoTuongOnline.Infrastructure;

/// <summary>
/// Giới hạn tần suất gọi API tài khoản theo địa chỉ IP (có sẵn trong .NET 8, không cần cài gói):
/// tối đa 30 request/phút cho nhóm /api/v1/auth/* (đăng nhập, đăng ký, OTP, quên mật khẩu).
/// Kết hợp với LoginAttemptTracker (khoá theo tài khoản) để chống dò mật khẩu.
/// </summary>
public static class RateLimitSetup
{
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, _) =>
            {
                await ExceptionMiddleware.WriteAsync(context.HttpContext, HttpStatusCode.TooManyRequests,
                    ErrorCodes.TooManyRequests, "Bạn thao tác quá nhiều lần. Vui lòng đợi 1 phút rồi thử lại.");
            };
            options.AddPolicy(AuthPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
        return services;
    }

    /// <summary>
    /// IP của người dùng. Khi chạy qua Cloudflare Tunnel, mọi request đều tới từ 127.0.0.1 (tiến trình cloudflared),
    /// nên lấy IP thật trong header CF-Connecting-IP — chỉ tin header này khi request đến từ chính máy chủ.
    /// </summary>
    private static string ClientIp(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip is not null && IPAddress.IsLoopback(ip))
        {
            string forwarded = http.Request.Headers["CF-Connecting-IP"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded)) return forwarded;
        }
        return ip?.ToString() ?? "unknown";
    }
}
