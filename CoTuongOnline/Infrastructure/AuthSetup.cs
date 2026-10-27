using System.Net;
using System.Text;
using CoTuongOnline.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

namespace CoTuongOnline.Infrastructure;

public static class AuthSetup
{
    /// <summary>
    /// Xác thực JWT (JwtBearer). REST gửi token qua header Authorization: Bearer;
    /// SignalR (WebSocket) không gửi header được nên token đi qua query ?access_token= trên đường dẫn /hubs.
    /// </summary>
    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration config)
    {
        var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
            throw new InvalidOperationException("Thiếu cấu hình Jwt:Key (tối thiểu 32 ký tự). Đặt trong appsettings hoặc biến môi trường Jwt__Key.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; // giữ nguyên tên claim "sub", "nickname"
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimsExtensions.NicknameClaim
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        // Trả JSON theo chuẩn { success, code, message } thay vì 401 rỗng
                        context.HandleResponse();
                        await ExceptionMiddleware.WriteAsync(context.HttpContext, HttpStatusCode.Unauthorized,
                            ErrorCodes.Unauthorized, "Bạn cần đăng nhập (hoặc phiên đăng nhập đã hết hạn).");
                    }
                };
            });

        services.AddAuthorization();
        services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();
        return services;
    }
}

/// <summary>Clients.User(userId) của SignalR dùng claim "sub" (userId) của JWT.</summary>
public sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(ClaimsExtensions.SubClaim)?.Value;
}
