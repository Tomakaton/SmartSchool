using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartSchool.API.Models;
using SmartSchool.API.Options;
using SmartSchool.API.Services;

namespace SmartSchool.Tests.Services;

[TestFixture]
public class TokenServiceTests
{
    private static readonly JwtSettings Settings = new()
    {
        Issuer = "SmartSchool.Tests",
        Audience = "SmartSchool.Tests.Client",
        SigningKey = "unit-test-signing-key-that-is-long-enough",
        AccessTokenExpiryMinutes = 15,
        RefreshTokenExpiryDays = 7
    };

    private readonly TokenService _tokenService = new(Options.Create(Settings));

    [Test]
    public void CreateAccessToken_ReturnsValidJwtWithUserClaims()
    {
        var user = new User
        {
            Email = "student@example.com",
            PasswordHash = "irrelevant"
        };

        var result = _tokenService.CreateAccessToken(user, ["Teacher", "Student"]);

        var token = Validate(result.Value);

        Assert.Multiple(() =>
        {
            Assert.That(token.Issuer, Is.EqualTo(Settings.Issuer));
            Assert.That(token.Audiences, Contains.Item(Settings.Audience));
            Assert.That(token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value,
                Is.EqualTo(user.Id.ToString()));
            Assert.That(token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Email).Value,
                Is.EqualTo(user.Email));
            Assert.That(token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Jti).Value,
                Is.Not.Empty);
            Assert.That(GetRoleClaims(token), Is.EquivalentTo(["Teacher", "Student"]));
        });
    }

    [Test]
    public void CreateAccessToken_ReturnsExpiryMatchingConfiguredLifetime()
    {
        var user = new User { Email = "student@example.com", PasswordHash = "irrelevant" };

        var before = DateTime.UtcNow;
        var result = _tokenService.CreateAccessToken(user, []);
        var after = DateTime.UtcNow;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExpiresAt,
                Is.EqualTo(before.AddMinutes(Settings.AccessTokenExpiryMinutes)).Within(TimeSpan.FromMinutes(1)));
            Assert.That(result.ExpiresAt, Is.GreaterThan(after));
            Assert.That(Validate(result.Value).ValidTo,
                Is.EqualTo(result.ExpiresAt).Within(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void CreateAccessToken_WithNoRoles_HasNoRoleClaims()
    {
        var user = new User { Email = "student@example.com", PasswordHash = "irrelevant" };

        var result = _tokenService.CreateAccessToken(user, []);

        Assert.That(GetRoleClaims(Validate(result.Value)), Is.Empty);
    }

    [Test]
    public void CreateRefreshToken_ReturnsUrlSafeBase64Encoded64Bytes()
    {
        var token = _tokenService.CreateRefreshToken();

        var bytes = WebEncoders.Base64UrlDecode(token);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Has.Length.EqualTo(64));
            Assert.That(token, Does.Not.Contain("+"));
            Assert.That(token, Does.Not.Contain("/"));
            Assert.That(token, Does.Not.Contain("="));
        });
    }

    [Test]
    public void CreateRefreshToken_ReturnsUniqueValues()
    {
        var tokens = Enumerable.Range(0, 100)
            .Select(_ => _tokenService.CreateRefreshToken())
            .ToHashSet();

        Assert.That(tokens, Has.Count.EqualTo(100));
    }

    [Test]
    public void HashRefreshToken_ReturnsUppercaseSha256HexDigest()
    {
        const string refreshToken = "abc";
        const string expected = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

        var hash = _tokenService.HashRefreshToken(refreshToken);

        Assert.Multiple(() =>
        {
            Assert.That(hash, Is.EqualTo(expected));
            Assert.That(hash, Is.EqualTo(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)))));
            Assert.That(hash, Has.Length.EqualTo(64));
        });
    }

    [Test]
    public void HashRefreshToken_IsDeterministicAndInputSensitive()
    {
        var first = _tokenService.HashRefreshToken("first-token");
        var second = _tokenService.HashRefreshToken("first-token");
        var other = _tokenService.HashRefreshToken("second-token");

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(first));
        });
    }

    private JwtSecurityToken Validate(string accessToken)
    {
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Settings.Issuer,
            ValidateAudience = true,
            ValidAudience = Settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Settings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        handler.ValidateToken(accessToken, validationParameters, out var validatedToken);

        return (JwtSecurityToken)validatedToken;
    }

    private static IEnumerable<string> GetRoleClaims(JwtSecurityToken token) =>
        token.Claims
            .Where(claim => claim.Type is ClaimTypes.Role or "role")
            .Select(claim => claim.Value);
}
