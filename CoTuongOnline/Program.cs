using System.Net;
using CoTuongOnline.Common;
using CoTuongOnline.Data;
using CoTuongOnline.Hubs;
using CoTuongOnline.Infrastructure;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// ---------------- API + định dạng lỗi validate ----------------
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var first = context.ModelState.Values.SelectMany(v => v.Errors)
            .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Dữ liệu gửi lên không hợp lệ." : e.ErrorMessage)
            .FirstOrDefault() ?? "Dữ liệu gửi lên không hợp lệ.";
        return new BadRequestObjectResult(ApiResponse.Fail(ErrorCodes.ValidationError, first));
    };
});

// ---------------- SignalR ----------------
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(5);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(15); // rớt mạng → phát hiện sau 15 giây
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddAppRateLimiting();
builder.Services.AddApiDocs();
builder.Services.AddAppServices(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
if (app.Environment.IsDevelopment()) app.UseApiDocs();

app.UseDefaultFiles();   // "/" → wwwroot/index.html
app.UseStaticFiles();    // các trang HTML/CSS/JS trong wwwroot

app.UseRouting();
app.UseRateLimiter();   // giới hạn tần suất API tài khoản (chống dò mật khẩu)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

// API không tồn tại → JSON 404 thay vì trang trống
app.Map("/api/{**rest}", (HttpContext ctx) =>
    ExceptionMiddleware.WriteAsync(ctx, HttpStatusCode.NotFound, ErrorCodes.NotFound, "API không tồn tại."));

await app.InitializeDatabaseAsync(); // tạo database + bảng nếu chưa có
await app.RecoverAfterRestartAsync(); // ván online dở → ABORTED, xoá tài khoản chưa xác nhận quá 24h
await app.SeedDemoDataAsync();
app.Run();
