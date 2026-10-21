using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CoTuongOnline.Infrastructure;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public static class CurrentUser
{
    public static string UserId(this ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new ApiException(401, "Vui lòng đăng nhập.");
}
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
    {
        int status = exception switch { ApiException e => e.Status, DbUpdateConcurrencyException => 409, _ => 500 };
        string message = exception switch {
            ApiException e => e.Message,
            DbUpdateConcurrencyException => "Phòng đã thay đổi. Vui lòng tải lại.",
            _ => "Không thể hoàn tất yêu cầu. Vui lòng thử lại."
        };
        if (status == 500) logger.LogError(exception, "Request failed: {Path}", context.Request.Path);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = message }, token);
        return true;
    }
}
// Cookie-authenticated writes require CSRF protection; bearer-only clients do not.
public sealed class CookieAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)) return;
        // Login and registration also require a token, preventing login CSRF.
        bool bearerOnly = request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && !request.Cookies.ContainsKey("xiangqi_token");
        if (bearerOnly) return;
        try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
        catch (AntiforgeryValidationException) {
            context.Result = new ObjectResult(new ProblemDetails { Status = 400, Title = "Phiên biểu mẫu không hợp lệ. Vui lòng tải lại trang." }) { StatusCode = 400 };
        }
    }
}
