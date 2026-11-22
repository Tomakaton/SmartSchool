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
    public void CreateAccessToken_ReturnsValidJwtWithIdentityAndStampClaims()
    {
        var fixedNow = new DateTimeOffset(2026, 11, 22, 15, 30, 0, TimeSpan.Zero);
        var tokenService = new TokenService(Options.Create(Settings), new FixedTimeProvider(fixedNow));
        var user = new User
        {
            Email = "student@example.com",
            PasswordHash = "irrelevant",
            IsActivated = true
        };

        var result = tokenService.CreateAccessToken(user);

        var token = Validate(result.Value, validateLifetime: false);

        Assert.Multiple(() =>
        {
            Assert.That(token.Issuer, Is.EqualTo(Settings.Issuer));
            Assert.That(token.Audiences, Contains.Item(Settings.Audience));
            Assert.That(
                token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value,
                Is.EqualTo(user.Id.ToString()));
            Assert.That(
                Guid.TryParse(
                    token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Jti).Value,
                    out _),
                Is.True);
            Assert.That(
                token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Iat).Value,
                Is.EqualTo(EpochTime.GetIntDate(fixedNow.UtcDateTime).ToString(CultureInfo.InvariantCulture)));
            Assert.That(
                token.Claims.Single(claim => claim.Type == TokenClaimTypes.SecurityStamp).Value,
                Is.EqualTo(user.SecurityStamp));
            Assert.That(
                token.Claims.Any(claim => claim.Type == JwtRegisteredClaimNames.Email),
                Is.False);
            Assert.That(GetRoleClaims(token), Is.Empty);
        });
    }

    [Test]
    public void CreateAccessToken_ReturnsExpiryMatchingConfiguredLifetime()
    {
        var fixedNow = new DateTimeOffset(2026, 11, 22, 15, 30, 0, TimeSpan.Zero);
        var tokenService = new TokenService(Options.Create(Settings), new FixedTimeProvider(fixedNow));
        var user = new User { Email = "student@example.com", PasswordHash = "irrelevant" };
        var expectedExpiry = fixedNow.UtcDateTime.AddMinutes(Settings.AccessTokenExpiryMinutes);

        var result = tokenService.CreateAccessToken(user);
        var token = Validate(result.Value, validateLifetime: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExpiresAt, Is.EqualTo(expectedExpiry));
            Assert.That(token.ValidTo, Is.EqualTo(expectedExpiry));
        });
    }

    [Test]
    public void CreateAccessToken_NeverEmbedsRoleClaimsEvenWhenUserHasRoleAssignments()
    {
        var teacherRole = new Role { Id = 1, Name = RoleNames.Teacher, IsActive = true };
        var adminRole = new Role { Id = 2, Name = RoleNames.PlatformAdmin, IsActive = true };
        var user = new User { Email = "teacher@example.com", PasswordHash = "irrelevant" };
        user.RoleAssignments.Add(new RoleAssignment
        {
            UserId = user.Id,
            RoleId = teacherRole.Id,
            Role = teacherRole,
            InstitutionId = Guid.NewGuid(),
            ValidFrom = new DateOnly(2026, 9, 1)
        });
        user.RoleAssignments.Add(new RoleAssignment
        {
            UserId = user.Id,
            RoleId = adminRole.Id,
            Role = adminRole,
            InstitutionId = null,
            ValidFrom = new DateOnly(2026, 9, 1)
        });

        var result = _tokenService.CreateAccessToken(user);
        var token = Validate(result.Value);

        Assert.Multiple(() =>
        {
            Assert.That(GetRoleClaims(token), Is.Empty);
            Assert.That(
                token.Claims.Select(claim => claim.Type),
                Is.EquivalentTo([
                    JwtRegisteredClaimNames.Sub,
                    JwtRegisteredClaimNames.Jti,
                    JwtRegisteredClaimNames.Iat,
                    TokenClaimTypes.SecurityStamp,
                    JwtRegisteredClaimNames.Exp,
                    JwtRegisteredClaimNames.Iss,
                    JwtRegisteredClaimNames.Aud
                ]));
        });
    }

    [Test]
    public void CreateAccessToken_ReflectsRotatedSecurityStamp()
    {
        var user = new User { Email = "student@example.com", PasswordHash = "irrelevant" };
        var initialStamp = user.SecurityStamp;

        var firstToken = Validate(_tokenService.CreateAccessToken(user).Value);
        user.RotateSecurityStamp();
        var secondToken = Validate(_tokenService.CreateAccessToken(user).Value);

        Assert.Multiple(() =>
        {
            Assert.That(
                firstToken.Claims.Single(claim => claim.Type == TokenClaimTypes.SecurityStamp).Value,
                Is.EqualTo(initialStamp));
            Assert.That(
                secondToken.Claims.Single(claim => claim.Type == TokenClaimTypes.SecurityStamp).Value,
                Is.EqualTo(user.SecurityStamp));
            Assert.That(user.SecurityStamp, Is.Not.EqualTo(initialStamp));
        });
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

    private static JwtSecurityToken Validate(string accessToken, bool validateLifetime = true)
    {
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Settings.Issuer,
            ValidateAudience = true,
            ValidAudience = Settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Settings.SigningKey)),
            ValidateLifetime = validateLifetime,
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
