using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CoTuongOnline.Common;

/// <summary>Bắt mọi exception trong pipeline REST và trả về JSON { success:false, code, message }.</summary>
public sealed class ExceptionMiddleware
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            await WriteAsync(context, ex.Status, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không mong muốn tại {Path}", context.Request.Path);
            await WriteAsync(context, HttpStatusCode.InternalServerError, ErrorCodes.Internal, "Máy chủ gặp lỗi, vui lòng thử lại.");
        }
    }

    public static async Task WriteAsync(HttpContext context, HttpStatusCode status, string code, string message)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(ApiResponse.Fail(code, message), Json));
    }
}

public static class ClaimsExtensions
{
    public const string SubClaim = "sub";
    public const string NicknameClaim = "nickname";
    public const string EmailClaim = "email";

    public static int GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst(SubClaim)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out var id)
            ? id
            : throw new AppException(ErrorCodes.Unauthorized, "Phiên đăng nhập không hợp lệ.", HttpStatusCode.Unauthorized);
    }

    public static string GetNickname(this ClaimsPrincipal user) =>
        user.FindFirst(NicknameClaim)?.Value ?? user.Identity?.Name ?? "?";
}
