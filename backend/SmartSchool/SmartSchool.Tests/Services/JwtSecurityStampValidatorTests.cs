using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartSchool.API.Contracts.Auth;
using SmartSchool.API.Data;
using SmartSchool.API.Models;
using SmartSchool.API.Options;
using SmartSchool.API.Services;

namespace SmartSchool.Tests.Services;

[TestFixture]
public class JwtSecurityStampValidatorTests
{
    private static readonly JwtSettings Settings = new()
    {
        Issuer = "SmartSchool.Tests",
        Audience = "SmartSchool.Tests.Client",
        SigningKey = "unit-test-signing-key-that-is-long-enough",
        AccessTokenExpiryMinutes = 15,
        RefreshTokenExpiryDays = 7
    };

    private ServiceProvider _serviceProvider = null!;
    private ApplicationDbContext _dbContext = null!;
    private TokenService _tokenService = null!;

    [SetUp]
    public void SetUp()
    {
        var dbName = $"security-stamp-validator-tests-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<ApplicationDbContext>();
        _tokenService = new TokenService(Options.Create(Settings));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }

    [Test]
    public async Task ValidateAsync_WithActiveUserAndMatchingStamp_Succeeds()
    {
        var user = await AddUserAsync(isActivated: true);
        var context = CreateContextFromToken(_tokenService.CreateAccessToken(user).Value);

        await JwtSecurityStampValidator.ValidateAsync(context);

        Assert.That(context.Result?.Failure, Is.Null);
    }

    [Test]
    public async Task ValidateAsync_AfterSecurityStampRotation_FailsValidation()
    {
        var user = await AddUserAsync(isActivated: true);
        var issuedToken = _tokenService.CreateAccessToken(user).Value;

        user.RotateSecurityStamp();
        await _dbContext.SaveChangesAsync();

        var context = CreateContextFromToken(issuedToken);
        await JwtSecurityStampValidator.ValidateAsync(context);

        Assert.That(context.Result?.Failure, Is.Not.Null);
    }

    [Test]
    public async Task ValidateAsync_AfterPasswordChange_RejectsPreviouslyIssuedAccessToken()
    {
        var passwordHasher = new PasswordHasher<User>();
        var user = new User
        {
            Email = "student@example.com",
            PasswordHash = string.Empty,
            IsActivated = true,
            DateCreated = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, "OldPassword123!");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var authService = new AuthService(
            _dbContext,
            _tokenService,
            passwordHasher,
            Options.Create(Settings));

        var loginResult = await authService.LoginAsync(
            new LoginRequest("student@example.com", "OldPassword123!"));
        var preChangeContext = CreateContextFromToken(loginResult.Value!.AccessToken);

        var changeResult = await authService.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("OldPassword123!", "NewPassword123!"));

        await JwtSecurityStampValidator.ValidateAsync(preChangeContext);

        Assert.Multiple(() =>
        {
            Assert.That(changeResult.Succeeded, Is.True, changeResult.Error);
            Assert.That(preChangeContext.Result?.Failure, Is.Not.Null);
        });
    }

    [Test]
    public async Task ValidateAsync_WhenUserIsNotActivated_FailsValidation()
    {
        var user = await AddUserAsync(isActivated: false);
        var context = CreateContextFromToken(_tokenService.CreateAccessToken(user).Value);

        await JwtSecurityStampValidator.ValidateAsync(context);

        Assert.That(context.Result?.Failure, Is.Not.Null);
    }

    [Test]
    public async Task ValidateAsync_WhenUserDoesNotExist_FailsValidation()
    {
        var unsavedUser = new User
        {
            Email = "ghost@example.com",
            PasswordHash = "hash",
            IsActivated = true
        };
        var context = CreateContextFromToken(_tokenService.CreateAccessToken(unsavedUser).Value);

        await JwtSecurityStampValidator.ValidateAsync(context);

        Assert.That(context.Result?.Failure, Is.Not.Null);
    }

    [Test]
    public async Task ValidateAsync_WhenSubClaimIsMissingOrMalformed_FailsValidation()
    {
        var missingSub = CreateContextWithClaims(
            new Claim(TokenClaimTypes.SecurityStamp, User.NewSecurityStamp()));
        var malformedSub = CreateContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid"),
            new Claim(TokenClaimTypes.SecurityStamp, User.NewSecurityStamp()));

        await JwtSecurityStampValidator.ValidateAsync(missingSub);
        await JwtSecurityStampValidator.ValidateAsync(malformedSub);

        Assert.Multiple(() =>
        {
            Assert.That(missingSub.Result?.Failure, Is.Not.Null);
            Assert.That(malformedSub.Result?.Failure, Is.Not.Null);
        });
    }

    [Test]
    public async Task ValidateAsync_WhenStampClaimIsMissingOrEmpty_FailsValidation()
    {
        var user = await AddUserAsync(isActivated: true);
        var missingStamp = CreateContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()));
        var emptyStamp = CreateContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(TokenClaimTypes.SecurityStamp, string.Empty));

        await JwtSecurityStampValidator.ValidateAsync(missingStamp);
        await JwtSecurityStampValidator.ValidateAsync(emptyStamp);

        Assert.Multiple(() =>
        {
            Assert.That(missingStamp.Result?.Failure, Is.Not.Null);
            Assert.That(emptyStamp.Result?.Failure, Is.Not.Null);
        });
    }

    private async Task<User> AddUserAsync(bool isActivated)
    {
        var user = new User
        {
            Email = "student@example.com",
            PasswordHash = "hash",
            IsActivated = isActivated,
            DateCreated = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return user;
    }

    private TokenValidatedContext CreateContextFromToken(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        return CreateContextCore(jwt.Claims, jwt);
    }

    private TokenValidatedContext CreateContextWithClaims(params Claim[] claims) =>
        CreateContextCore(claims, token: null);

    private TokenValidatedContext CreateContextCore(
        IEnumerable<Claim> claims,
        JwtSecurityToken? token)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = _serviceProvider
        };
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(JwtBearerHandler));

        return new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme.Name)),
            SecurityToken = token ?? new JwtSecurityToken()
        };
    }
}
