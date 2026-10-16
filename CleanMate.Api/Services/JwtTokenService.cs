using CleanMate.Api.Contracts;
using CleanMate.Api.Domain.Entities;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CleanMate.Api.Services;

public class JwtTokenService
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly SecurityKey _signingKey;

    public JwtTokenService(IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Missing Jwt:Key");

        _issuer = configuration["Jwt:Issuer"] ?? "CleanMate";
        _audience = configuration["Jwt:Audience"] ?? "CleanMate";
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    }

    public AuthResponseDto CreateAuthResponse(User user, UserResponseDto mappedUser)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("name", user.FullName),
            new Claim("token_version", user.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new AuthResponseDto(
            new JwtSecurityTokenHandler().WriteToken(token),
            mappedUser);
    }
}
