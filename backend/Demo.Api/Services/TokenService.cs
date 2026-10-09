using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Demo.Api.Contracts;
using Demo.Api.Domain;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

namespace Demo.Api.Services;

public sealed class JwtOptions
{
    public string Secret { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public int LifetimeMinutes { get; init; } = 60;
}

public sealed class TokenService(IOptions<JwtOptions> configuredOptions)
{
    private readonly JwtOptions options = configuredOptions.Value;
    public LoginResponse Create(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(options.LifetimeMinutes);
        var token = new JwtSecurityToken(
            issuer: options.Issuer, audience: options.Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim("role", user.Role.ToString()),
                new Claim("sv", user.SecurityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            notBefore: DateTime.UtcNow, expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret)), SecurityAlgorithms.HmacSha256));
        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expiresAt,
            new UserResponse(user.Id, user.Name, user.Email, user.Role, user.IsActive, user.CreatedAt, user.UpdatedAt));
    }
}
