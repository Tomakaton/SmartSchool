using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartSchool.API.Models;
using SmartSchool.API.Options;

namespace SmartSchool.API.Services;

/// <summary>
/// Creates signed JWT access tokens and opaque refresh tokens, and hashes refresh tokens
/// so only the digest is persisted.
/// </summary>
public class TokenService(
    IOptions<JwtSettings> jwtSettings,
    TimeProvider? timeProvider = null) : ITokenService
{
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public AccessTokenResult CreateAccessToken(User user)
    {
        var issuedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = issuedAt.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes);

        // Tokens carry identity and revocation state only; authorization resolves roles per request.
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(
                JwtRegisteredClaimNames.Iat,
                EpochTime.GetIntDate(issuedAt).ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            new(TokenClaimTypes.SecurityStamp, user.SecurityStamp)
        ];

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: signingCredentials);

        return new AccessTokenResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }

    // 512 bits of cryptographic entropy, base64url-encoded so the value is safe in JSON and headers.
    /// <inheritdoc />
    public string CreateRefreshToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));

    // The raw token is only ever returned to the client; storage keeps this digest.
    /// <inheritdoc />
    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
