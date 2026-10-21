using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CoTuongOnline.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace CoTuongOnline.Features.Accounts;

public sealed record Credentials(
    [Required, RegularExpression("^[a-zA-Z0-9_]{3,32}$")] string Username,
    [Required, MinLength(8), MaxLength(128)] string Password);

[ApiController, Route("api/auth"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(UserManager<IdentityUser> users, IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    [HttpGet("csrf"), AllowAnonymous]
    public object Csrf([FromServices] IAntiforgery antiforgery) => new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken };

    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("accounts")]
    public async Task<IActionResult> Register(Credentials input)
    {
        var user = new IdentityUser { UserName = input.Username };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded) throw new ApiException(400, "Tên đăng nhập đã tồn tại hoặc mật khẩu không hợp lệ (ít nhất 8 ký tự).");
        return Ok(await SignIn(user));
    }
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("accounts")]
    public async Task<IActionResult> Login(Credentials input)
    {
        var user = await users.FindByNameAsync(input.Username);
        if (user is null || await users.IsLockedOutAsync(user)) throw new ApiException(401, "Tên đăng nhập hoặc mật khẩu không đúng.");
        if (!await users.CheckPasswordAsync(user, input.Password)) {
            await users.AccessFailedAsync(user);
            throw new ApiException(401, "Tên đăng nhập hoặc mật khẩu không đúng.");
        }
        await users.ResetAccessFailedCountAsync(user);
        return Ok(await SignIn(user));
    }
    private async Task<object> SignIn(IdentityUser user)
    {
        var expires = DateTime.UtcNow.AddHours(8);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, user.UserName!),
            new Claim("stamp", await users.GetSecurityStampAsync(user)) };
        var jwt = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims,
            expires: expires, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        string token = new JwtSecurityTokenHandler().WriteToken(jwt);
        Response.Cookies.Append("xiangqi_token", token, new CookieOptions { HttpOnly = true, Secure = !env.IsDevelopment() || Request.IsHttps,
            SameSite = SameSiteMode.Strict, Expires = expires, Path = "/", IsEssential = true });
        // The UI uses the HttpOnly cookie; bearer clients may use this response token.
        return new { user = new { id = user.Id, username = user.UserName }, accessToken = token, expiresAt = expires };
    }
    [HttpGet("me"), Authorize]
    public object Me() => new { id = User.UserId(), username = User.Identity!.Name };
    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout()
    {
        var user = (await users.FindByIdAsync(User.UserId()))!;
        await users.UpdateSecurityStampAsync(user);
        Response.Cookies.Delete("xiangqi_token", new CookieOptions { Path = "/" });
        return NoContent();
    }
}
