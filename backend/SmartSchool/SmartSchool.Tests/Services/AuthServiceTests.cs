using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartSchool.API.Contracts.Auth;
using SmartSchool.API.Contracts.Common;
using SmartSchool.API.Data;
using SmartSchool.API.Models;
using SmartSchool.API.Options;
using SmartSchool.API.Services;

namespace SmartSchool.Tests.Services;

[TestFixture]
public class AuthServiceTests
{
    private static readonly JwtSettings Settings = new()
    {
        Issuer = "SmartSchool.Tests",
        Audience = "SmartSchool.Tests.Client",
        SigningKey = "unit-test-signing-key-that-is-long-enough",
        AccessTokenExpiryMinutes = 15,
        RefreshTokenExpiryDays = 7
    };

    private ApplicationDbContext _dbContext = null!;
    private IPasswordHasher<User> _passwordHasher = null!;
    private ITokenService _tokenService = null!;
    private AuthService _authService = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"auth-service-tests-{Guid.NewGuid()}")
                .Options);

        _passwordHasher = new PasswordHasher<User>();
        _tokenService = new TokenService(Options.Create(Settings));
        _authService = new AuthService(
            _dbContext,
            _tokenService,
            _passwordHasher,
            Options.Create(Settings));
    }

    [TearDown]
    public void TearDown() => _dbContext.Dispose();

    [Test]
    public async Task LoginAsync_WithValidCredentials_ReturnsTokensAndStoresOnlyTheRefreshTokenHash()
    {
        var role = await AddRoleAsync(1, "Teacher");
        var user = await AddUserAsync("student@example.com", "Password123!", role);

        var result = await _authService.LoginAsync(new LoginRequest("student@example.com", "Password123!"));

        Assert.That(result.Succeeded, Is.True, result.Error);
        var response = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(response.AccessToken, Is.Not.Empty);
            Assert.That(response.RefreshToken, Is.Not.Empty);
            Assert.That(response.AccessTokenExpiresAt,
                Is.EqualTo(DateTime.UtcNow.AddMinutes(15)).Within(TimeSpan.FromMinutes(1)));
            Assert.That(response.RefreshTokenExpiresAt,
                Is.EqualTo(DateTime.UtcNow.AddDays(7)).Within(TimeSpan.FromMinutes(1)));
            Assert.That(GetClaimValue(response.AccessToken, JwtRegisteredClaimNames.Sub),
                Is.EqualTo(user.Id.ToString()));
            Assert.That(GetClaimValue(response.AccessToken, TokenClaimTypes.SecurityStamp),
                Is.EqualTo(user.SecurityStamp));
            Assert.That(GetRoleClaims(response.AccessToken), Is.Empty);
        });

        var storedToken = await _dbContext.RefreshTokens.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(storedToken.UserId, Is.EqualTo(user.Id));
            Assert.That(storedToken.FamilyId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(storedToken.TokenHash, Is.EqualTo(_tokenService.HashRefreshToken(response.RefreshToken)));
            Assert.That(storedToken.TokenHash, Is.Not.EqualTo(response.RefreshToken));
            Assert.That(storedToken.RevokedAt, Is.Null);
            Assert.That(storedToken.CreatedAt, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
            Assert.That(storedToken.ExpiresAt,
                Is.EqualTo(DateTime.UtcNow.AddDays(7)).Within(TimeSpan.FromMinutes(1)));
        });
    }

    [Test]
    public async Task LoginAsync_WithUnknownEmail_ReturnsFailureWithoutRevealingCause()
    {
        var result = await _authService.LoginAsync(new LoginRequest("missing@example.com", "Password123!"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Error, Is.EqualTo("Invalid email or password."));
            Assert.That(_dbContext.RefreshTokens, Is.Empty);
        });
    }

    [Test]
    public async Task LoginAsync_WithWrongPassword_ReturnsFailureWithoutStoringAToken()
    {
        await AddUserAsync("student@example.com", "Password123!");

        var result = await _authService.LoginAsync(new LoginRequest("student@example.com", "WrongPassword123!"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo("Invalid email or password."));
            Assert.That(_dbContext.RefreshTokens, Is.Empty);
        });
    }

    [Test]
    public async Task LoginAsync_MatchesEmailCaseInsensitivelyAndIgnoresSurroundingWhitespace()
    {
        await AddUserAsync("Student@Example.com", "Password123!");

        var result = await _authService.LoginAsync(
            new LoginRequest("  student@example.com  ", "Password123!"));

        Assert.That(result.Succeeded, Is.True, result.Error);
    }

    [Test]
    public async Task LoginAsync_DoesNotEmbedRoleClaimsInAccessToken()
    {
        var activeRole = await AddRoleAsync(1, RoleNames.Teacher);
        var inactiveRole = await AddRoleAsync(2, RoleNames.InstitutionAdmin, isActive: false);
        var user = await AddUserAsync("student@example.com", "Password123!", activeRole, inactiveRole);

        var result = await _authService.LoginAsync(new LoginRequest("student@example.com", "Password123!"));

        Assert.That(result.Succeeded, Is.True, result.Error);
        var accessToken = result.Value!.AccessToken;
        Assert.Multiple(() =>
        {
            Assert.That(GetRoleClaims(accessToken), Is.Empty);
            Assert.That(GetClaimValue(accessToken, TokenClaimTypes.SecurityStamp),
                Is.EqualTo(user.SecurityStamp));
        });
    }

    [Test]
    public async Task LoginAsync_WhenPasswordNeedsRehash_UpdatesStoredHash()
    {
        await AddUserAsync("student@example.com", "Password123!");
        var rehashingService = new AuthService(
            _dbContext,
            _tokenService,
            new AlwaysRehashPasswordHasher(),
            Options.Create(Settings));

        var result = await rehashingService.LoginAsync(
            new LoginRequest("student@example.com", "Password123!"));

        Assert.That(result.Succeeded, Is.True, result.Error);
        var storedUser = await _dbContext.Users.SingleAsync();
        Assert.That(storedUser.PasswordHash, Is.EqualTo("rehashed:Password123!"));
    }

    [Test]
    public async Task RefreshTokenAsync_WithValidToken_RotatesTheTokenAndRevokesTheOldOne()
    {
        var role = await AddRoleAsync(1, "Teacher");
        var user = await AddUserAsync("student@example.com", "Password123!", role);
        var oldToken = await AddRefreshTokenAsync(user, "old-refresh-token", DateTime.UtcNow.AddDays(1));

        var result = await _authService.RefreshTokenAsync(
            new RefreshTokenRequest("old-refresh-token"));

        Assert.That(result.Succeeded, Is.True, result.Error);
        var response = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(response.AccessToken, Is.Not.Empty);
            Assert.That(response.RefreshToken, Is.Not.EqualTo("old-refresh-token"));
            Assert.That(GetClaimValue(response.AccessToken, TokenClaimTypes.SecurityStamp),
                Is.EqualTo(user.SecurityStamp));
            Assert.That(GetRoleClaims(response.AccessToken), Is.Empty);
        });

        var tokens = await _dbContext.RefreshTokens.ToListAsync();
        var persistedOld = tokens.Single(token => token.Id == oldToken.Id);
        var persistedNew = tokens.Single(token => token.Id != oldToken.Id);
        Assert.Multiple(() =>
        {
            Assert.That(tokens, Has.Count.EqualTo(2));
            Assert.That(persistedOld.RevokedAt, Is.Not.Null);
            Assert.That(persistedOld.ReplacedByTokenId, Is.EqualTo(persistedNew.Id));
            Assert.That(persistedNew.FamilyId, Is.EqualTo(persistedOld.FamilyId));
            Assert.That(persistedNew.RevokedAt, Is.Null);
            Assert.That(persistedNew.TokenHash,
                Is.EqualTo(_tokenService.HashRefreshToken(response.RefreshToken)));
            Assert.That(persistedNew.ExpiresAt,
                Is.EqualTo(DateTime.UtcNow.AddDays(7)).Within(TimeSpan.FromMinutes(1)));
        });
    }

    [Test]
    public async Task RefreshTokenAsync_WithAlreadyRotatedToken_RevokesEntireFamilyWithoutAffectingOtherSessions()
    {
        await AddUserAsync("student@example.com", "Password123!");

        var firstLogin = await _authService.LoginAsync(new LoginRequest("student@example.com", "Password123!"));
        var secondLogin = await _authService.LoginAsync(new LoginRequest("student@example.com", "Password123!"));

        var firstRotated = await _authService.RefreshTokenAsync(
            new RefreshTokenRequest(firstLogin.Value!.RefreshToken));
        var secondRotated = await _authService.RefreshTokenAsync(
            new RefreshTokenRequest(firstRotated.Value!.RefreshToken));

        Assert.That(secondRotated.Succeeded, Is.True, secondRotated.Error);

        // Replay the very first token from the first login session.
        var replayResult = await _authService.RefreshTokenAsync(
            new RefreshTokenRequest(firstLogin.Value.RefreshToken));

        var latestFirstFamilyHash = _tokenService.HashRefreshToken(secondRotated.Value!.RefreshToken);
        var secondSessionHash = _tokenService.HashRefreshToken(secondLogin.Value!.RefreshToken);

        var latestFirstFamilyToken = await _dbContext.RefreshTokens
            .SingleAsync(token => token.TokenHash == latestFirstFamilyHash);
        var secondSessionToken = await _dbContext.RefreshTokens
            .SingleAsync(token => token.TokenHash == secondSessionHash);

        Assert.Multiple(() =>
        {
            Assert.That(replayResult.Succeeded, Is.False);
            Assert.That(replayResult.ErrorKind, Is.EqualTo(ResultErrorKind.Unauthorized));
            Assert.That(latestFirstFamilyToken.FamilyId, Is.Not.EqualTo(secondSessionToken.FamilyId));
            Assert.That(latestFirstFamilyToken.RevokedAt, Is.Not.Null);
            Assert.That(secondSessionToken.RevokedAt, Is.Null);
        });
    }

    [Test]
    public async Task RefreshTokenAsync_WhenConcurrentModificationOccurs_ReturnsUnauthorized()
    {
        var dbName = $"auth-concurrency-tests-{Guid.NewGuid()}";
        await using var conflictContext = new ConcurrencyConflictDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options);

        var user = new User
        {
            Email = "student@example.com",
            PasswordHash = _passwordHasher.HashPassword(null!, "Password123!"),
            IsActivated = true,
            DateCreated = DateTime.UtcNow
        };
        const string rawToken = "concurrent-refresh-token";
        conflictContext.Users.Add(user);
        conflictContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashRefreshToken(rawToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await conflictContext.SaveChangesAsync();

        conflictContext.ThrowConcurrencyOnNextSave = true;

        var service = new AuthService(
            conflictContext,
            _tokenService,
            _passwordHasher,
            Options.Create(Settings));

        var result = await service.RefreshTokenAsync(new RefreshTokenRequest(rawToken));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorKind, Is.EqualTo(ResultErrorKind.Unauthorized));
            Assert.That(result.Error, Is.EqualTo("Invalid or expired refresh token."));
        });
    }

    [Test]
    public async Task RefreshTokenAsync_WithUnknownToken_ReturnsFailure()
    {
        await AddUserAsync("student@example.com", "Password123!");

        var result = await _authService.RefreshTokenAsync(new RefreshTokenRequest("unknown-token"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo("Invalid or expired refresh token."));
        });
    }

    [Test]
    public async Task RefreshTokenAsync_WithRawHashInsteadOfToken_ReturnsFailure()
    {
        var user = await AddUserAsync("student@example.com", "Password123!");
        const string rawToken = "raw-refresh-token";
        await AddRefreshTokenAsync(user, rawToken, DateTime.UtcNow.AddDays(1));

        var result = await _authService.RefreshTokenAsync(
            new RefreshTokenRequest(_tokenService.HashRefreshToken(rawToken)));

        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task RefreshTokenAsync_WithRevokedToken_ReturnsFailure()
    {
        var user = await AddUserAsync("student@example.com", "Password123!");
        await AddRefreshTokenAsync(
            user,
            "revoked-token",
            DateTime.UtcNow.AddDays(1),
            revokedAt: DateTime.UtcNow.AddHours(-1));

        var result = await _authService.RefreshTokenAsync(new RefreshTokenRequest("revoked-token"));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(await _dbContext.RefreshTokens.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task RefreshTokenAsync_WithExpiredToken_ReturnsFailure()
    {
        var user = await AddUserAsync("student@example.com", "Password123!");
        await AddRefreshTokenAsync(user, "expired-token", DateTime.UtcNow.AddDays(-1));

        var result = await _authService.RefreshTokenAsync(new RefreshTokenRequest("expired-token"));

        Assert.That(result.Succeeded, Is.False);
        Assert.That(await _dbContext.RefreshTokens.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task ChangePasswordAsync_WithCorrectCurrentPassword_UpdatesHashAndRevokesActiveTokens()
    {
        var user = await AddUserAsync("student@example.com", "OldPassword123!");
        var initialStamp = user.SecurityStamp;
        var activeToken = await AddRefreshTokenAsync(user, "active-token", DateTime.UtcNow.AddDays(1));
        var expiredToken = await AddRefreshTokenAsync(user, "expired-token", DateTime.UtcNow.AddDays(-1));
        var alreadyRevokedAt = DateTime.UtcNow.AddHours(-2);
        var revokedToken = await AddRefreshTokenAsync(
            user,
            "revoked-token",
            DateTime.UtcNow.AddDays(1),
            alreadyRevokedAt);

        var result = await _authService.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("OldPassword123!", "NewPassword123!"));

        Assert.That(result.Succeeded, Is.True, result.Error);

        var storedUser = await _dbContext.Users.SingleAsync(stored => stored.Id == user.Id);
        Assert.Multiple(() =>
        {
            Assert.That(
                _passwordHasher.VerifyHashedPassword(storedUser, storedUser.PasswordHash, "NewPassword123!"),
                Is.Not.EqualTo(PasswordVerificationResult.Failed));
            Assert.That(
                _passwordHasher.VerifyHashedPassword(storedUser, storedUser.PasswordHash, "OldPassword123!"),
                Is.EqualTo(PasswordVerificationResult.Failed));
            Assert.That(storedUser.SecurityStamp, Is.Not.EqualTo(initialStamp));
            Assert.That(storedUser.SecurityStamp, Has.Length.EqualTo(32));
        });

        var tokens = await _dbContext.RefreshTokens.ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(tokens.Single(token => token.Id == activeToken.Id).RevokedAt, Is.Not.Null);
            Assert.That(tokens.Single(token => token.Id == expiredToken.Id).RevokedAt, Is.Not.Null);
            Assert.That(tokens.Single(token => token.Id == revokedToken.Id).RevokedAt,
                Is.EqualTo(alreadyRevokedAt));
        });
    }

    [Test]
    public async Task ChangePasswordAsync_WithIncorrectCurrentPassword_LeavesUserAndTokensUntouched()
    {
        var user = await AddUserAsync("student@example.com", "OldPassword123!");
        var initialStamp = user.SecurityStamp;
        var token = await AddRefreshTokenAsync(user, "active-token", DateTime.UtcNow.AddDays(1));

        var result = await _authService.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("WrongPassword123!", "NewPassword123!"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo("Current password is incorrect."));
        });

        var storedUser = await _dbContext.Users.SingleAsync(stored => stored.Id == user.Id);
        var storedToken = await _dbContext.RefreshTokens.SingleAsync(stored => stored.Id == token.Id);
        Assert.Multiple(() =>
        {
            Assert.That(
                _passwordHasher.VerifyHashedPassword(storedUser, storedUser.PasswordHash, "OldPassword123!"),
                Is.Not.EqualTo(PasswordVerificationResult.Failed));
            Assert.That(storedToken.RevokedAt, Is.Null);
            Assert.That(storedUser.SecurityStamp, Is.EqualTo(initialStamp));
        });
    }

    [Test]
    public async Task LoginAsync_WhenAccountPendingActivationWithNoPassword_ReturnsForbidden()
    {
        var user = new User
        {
            Email = "pending@example.com",
            PasswordHash = string.Empty,
            IsActivated = false,
            DateCreated = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var result = await _authService.LoginAsync(new LoginRequest("pending@example.com", "Anything123!"));
        var refreshTokenCount = await _dbContext.RefreshTokens.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorKind, Is.EqualTo(ResultErrorKind.Forbidden));
            Assert.That(refreshTokenCount, Is.Zero);
        });
    }

    [Test]
    public async Task LoginAsync_WhenDeactivatedAccountSuppliesWrongPassword_ReturnsUnauthorized()
    {
        var user = await AddUserAsync("inactive@example.com", "CorrectPassword123!");
        user.IsActivated = false;
        await _dbContext.SaveChangesAsync();

        var result = await _authService.LoginAsync(new LoginRequest("inactive@example.com", "WrongPassword123!"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorKind, Is.EqualTo(ResultErrorKind.Unauthorized));
            Assert.That(result.Error, Is.EqualTo("Invalid email or password."));
        });
    }

    [Test]
    public async Task LoginAsync_WhenDeactivatedAccountSuppliesCorrectPassword_ReturnsForbidden()
    {
        var user = await AddUserAsync("inactive@example.com", "CorrectPassword123!");
        user.IsActivated = false;
        await _dbContext.SaveChangesAsync();

        var result = await _authService.LoginAsync(new LoginRequest("inactive@example.com", "CorrectPassword123!"));
        var refreshTokenCount = await _dbContext.RefreshTokens.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorKind, Is.EqualTo(ResultErrorKind.Forbidden));
            Assert.That(refreshTokenCount, Is.Zero);
        });
    }

    [Test]
    public async Task RefreshTokenAsync_WhenUserIsDeactivated_FailsWithoutRotating()
    {
        var user = await AddUserAsync("inactive@example.com", "Password123!");
        var token = await AddRefreshTokenAsync(user, "valid-refresh-token", DateTime.UtcNow.AddDays(1));
        user.IsActivated = false;
        await _dbContext.SaveChangesAsync();

        var result = await _authService.RefreshTokenAsync(new RefreshTokenRequest("valid-refresh-token"));

        var storedToken = await _dbContext.RefreshTokens.SingleAsync(stored => stored.Id == token.Id);
        var refreshTokenCount = await _dbContext.RefreshTokens.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorKind, Is.EqualTo(ResultErrorKind.Unauthorized));
            Assert.That(storedToken.RevokedAt, Is.Null);
            Assert.That(refreshTokenCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ChangePasswordAsync_WithUnknownUser_ReturnsFailure()
    {
        var result = await _authService.ChangePasswordAsync(
            Guid.NewGuid(),
            new ChangePasswordRequest("OldPassword123!", "NewPassword123!"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo("User was not found."));
        });
    }

    private async Task<Role> AddRoleAsync(long id, string name, bool isActive = true)
    {
        var role = new Role
        {
            Id = id,
            Name = name,
            IsActive = isActive
        };

        _dbContext.Roles.Add(role);
        await _dbContext.SaveChangesAsync();

        return role;
    }

    private async Task<User> AddUserAsync(string email, string password, params Role[] roles)
    {
        var user = new User
        {
            Email = email,
            PasswordHash = string.Empty,
            IsActivated = true,
            DateCreated = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        foreach (var role in roles)
        {
            user.RoleAssignments.Add(new RoleAssignment
            {
                UserId = user.Id,
                RoleId = role.Id,
                Role = role,
                ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))
            });
        }

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return user;
    }

    private async Task<RefreshToken> AddRefreshTokenAsync(
        User user,
        string rawToken,
        DateTime expiresAt,
        DateTime? revokedAt = null)
    {
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashRefreshToken(rawToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        return refreshToken;
    }

    private static string? GetClaimValue(string accessToken, string claimType) =>
        new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims
            .SingleOrDefault(claim => claim.Type == claimType)
            ?.Value;

    private static IEnumerable<string> GetRoleClaims(string accessToken) =>
        new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims
            .Where(claim => claim.Type is ClaimTypes.Role or "role")
            .Select(claim => claim.Value);

    private sealed class AlwaysRehashPasswordHasher : IPasswordHasher<User>
    {
        public string HashPassword(User user, string password) => $"rehashed:{password}";

        public PasswordVerificationResult VerifyHashedPassword(
            User user,
            string hashedPassword,
            string providedPassword) => PasswordVerificationResult.SuccessRehashNeeded;
    }

    private sealed class ConcurrencyConflictDbContext(
        DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        public bool ThrowConcurrencyOnNextSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowConcurrencyOnNextSave)
            {
                ThrowConcurrencyOnNextSave = false;
                throw new DbUpdateConcurrencyException("Simulated concurrent refresh.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
