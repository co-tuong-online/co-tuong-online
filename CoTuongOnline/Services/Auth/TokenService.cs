using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CoTuongOnline.Common;
using CoTuongOnline.Infrastructure;
using CoTuongOnline.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CoTuongOnline.Services.Auth;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(User user);
}

/// <summary>Tạo JWT HS256 chứa claim sub (userId), nickname, email.</summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public (string Token, DateTime ExpiresAt) CreateToken(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpireMinutes);
        var claims = new[]
        {
            new Claim(ClaimsExtensions.SubClaim, user.Id.ToString()),
            new Claim(ClaimsExtensions.NicknameClaim, user.Nickname),
            new Claim(ClaimsExtensions.EmailClaim, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
