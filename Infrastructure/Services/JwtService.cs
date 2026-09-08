using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Common.Helpers;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Creates JWT access tokens and refresh token secrets.
/// </summary>
public sealed class JwtService(
    IOptions<JwtOptions> options,
    TimeProvider timeProvider)
    : IJwtService
{
    private readonly JwtOptions _options = options.Value;

    /// <inheritdoc />
    public JwtTokenResultDto GenerateAccessToken(Guid userId, string email, IReadOnlyCollection<string> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentNullException.ThrowIfNull(roles);

        var utcNow = timeProvider.GetUtcNow();
        var expiresAtUtc = utcNow.AddMinutes(_options.AccessTokenMinutes);
        var jwtId = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Email, email),
            new(JwtRegisteredClaimNames.Jti, jwtId)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: utcNow.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials);

        return new JwtTokenResultDto(new JwtSecurityTokenHandler().WriteToken(token), jwtId, expiresAtUtc);
    }

    /// <inheritdoc />
    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(bytes);
    }

    /// <inheritdoc />
    public string HashRefreshToken(string refreshToken) => HashHelper.ComputeSha256Hash(refreshToken);

    /// <inheritdoc />
    public DateTimeOffset GetRefreshTokenExpiresAt(DateTimeOffset utcNow) => utcNow.AddDays(_options.RefreshTokenDays);

    /// <inheritdoc />
    public string GenerateEmailVerificationToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(bytes);
    }

    /// <inheritdoc />
    public string HashEmailVerificationToken(string verificationToken) =>
        HashHelper.ComputeSha256Hash(verificationToken);

    /// <inheritdoc />
    public DateTimeOffset GetEmailVerificationExpiresAt(DateTimeOffset utcNow) =>
        utcNow.AddHours(_options.EmailVerificationHours);

    /// <inheritdoc />
    public string GeneratePasswordResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(bytes);
    }

    /// <inheritdoc />
    public string HashPasswordResetToken(string resetToken) =>
        HashHelper.ComputeSha256Hash(resetToken);

    /// <inheritdoc />
    public DateTimeOffset GetPasswordResetExpiresAt(DateTimeOffset utcNow) =>
        utcNow.AddMinutes(_options.PasswordResetMinutes);

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
