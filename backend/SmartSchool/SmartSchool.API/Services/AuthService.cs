using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartSchool.API.Contracts.Auth;
using SmartSchool.API.Contracts.Common;
using SmartSchool.API.Data;
using SmartSchool.API.Models;
using SmartSchool.API.Options;

namespace SmartSchool.API.Services;

/// <summary>
/// Verifies credentials, issues and rotates token pairs, and changes passwords.
/// All expected failures are returned as <see cref="Result"/> values rather than exceptions.
/// </summary>
public class AuthService(
    ApplicationDbContext dbContext,
    ITokenService tokenService,
    IPasswordHasher<User> passwordHasher,
    IOptions<JwtSettings> jwtSettings) : IAuthService
{
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;

    /// <inheritdoc />
    public async Task<Result<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        // Compare lowercased values instead of using EF.Functions.ILike: ILike is Npgsql-only
        // (unit tests run on EF InMemory) and treats the input as a LIKE pattern, so '%' and '_'
        // in an email would act as wildcards. This does not use the Email index; add a functional
        // index on lower("Email") if email lookups become a bottleneck.
        var email = request.Identifier.Trim().ToLowerInvariant();

        var user = await dbContext.Users
            .FirstOrDefaultAsync(user => user.Email.ToLower() == email, cancellationToken);

        if (user is null)
        {
            return Result<LoginResponse>.Failure(
                "Invalid email or password.",
                ResultErrorKind.Unauthorized);
        }

        // Provisioned accounts that have not consumed a magic link yet have an empty PasswordHash;
        // surface 403 immediately so the client can direct them to their activation email. Accounts
        // that do hold a password hash verify the password first so wrong-password probes still get 401.
        if (!user.IsActivated && string.IsNullOrEmpty(user.PasswordHash))
        {
            return Result<LoginResponse>.Failure(
                "Account has not been activated.",
                ResultErrorKind.Forbidden);
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        switch (verificationResult)
        {
            case PasswordVerificationResult.Failed:
                return Result<LoginResponse>.Failure(
                    "Invalid email or password.",
                    ResultErrorKind.Unauthorized);
            case PasswordVerificationResult.SuccessRehashNeeded:
                if (user.IsActivated)
                {
                    user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
                }
                break;
            case PasswordVerificationResult.Success:
                break;
            default:
                return Result<LoginResponse>.Failure(
                    "Invalid login attempt.",
                    ResultErrorKind.Unauthorized);
        }

        if (!user.IsActivated)
        {
            return Result<LoginResponse>.Failure(
                "Account has not been activated.",
                ResultErrorKind.Forbidden);
        }

        var accessToken = tokenService.CreateAccessToken(user);
        var refreshToken = CreateRefreshToken(user);

        dbContext.RefreshTokens.Add(refreshToken.Entity);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<LoginResponse>.Success(new LoginResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Token,
            refreshToken.Entity.ExpiresAt));
    }

    /// <inheritdoc />
    public async Task<Result<RefreshTokenResponse>> RefreshTokenAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);

        var storedToken = await dbContext.RefreshTokens
            .Include(refreshToken => refreshToken.User)
            .FirstOrDefaultAsync(
                refreshToken => refreshToken.TokenHash == tokenHash,
                cancellationToken);

        if (storedToken is null)
        {
            return Result<RefreshTokenResponse>.Failure(
                "Invalid or expired refresh token.",
                ResultErrorKind.Unauthorized);
        }

        // Reuse detection: presenting a token that was already rotated means either the token was
        // stolen or an old copy was replayed. Revoke every active token in this family immediately.
        if (storedToken.ReplacedByTokenId is not null)
        {
            await RevokeFamilyAsync(storedToken.FamilyId, cancellationToken);

            return Result<RefreshTokenResponse>.Failure(
                "Invalid or expired refresh token.",
                ResultErrorKind.Unauthorized);
        }

        if (storedToken.RevokedAt is not null
            || storedToken.ExpiresAt <= DateTime.UtcNow
            || !storedToken.User.IsActivated)
        {
            return Result<RefreshTokenResponse>.Failure(
                "Invalid or expired refresh token.",
                ResultErrorKind.Unauthorized);
        }

        var user = storedToken.User;
        var accessToken = tokenService.CreateAccessToken(user);
        var refreshToken = CreateRefreshToken(user, storedToken.FamilyId);

        // Single-use rotation: revoke the token that was presented and link it to its replacement.
        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.ReplacedByToken = refreshToken.Entity;

        dbContext.RefreshTokens.Add(refreshToken.Entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent request already rotated or revoked this token; fail closed.
            return Result<RefreshTokenResponse>.Failure(
                "Invalid or expired refresh token.",
                ResultErrorKind.Unauthorized);
        }

        return Result<RefreshTokenResponse>.Success(new RefreshTokenResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Token,
            refreshToken.Entity.ExpiresAt));
    }

    /// <inheritdoc />
    public async Task<Result> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure("User was not found.");
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.CurrentPassword);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Result.Failure("Current password is incorrect.");
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.RotateSecurityStamp();

        // Revoke every token that has not been revoked yet. This includes expired tokens because
        // the filter only checks RevokedAt.
        var activeRefreshTokens = await dbContext.RefreshTokens
            .Where(refreshToken => refreshToken.UserId == userId && refreshToken.RevokedAt == null)
            .ToListAsync(cancellationToken);

        var revokedAt = DateTime.UtcNow;

        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.RevokedAt = revokedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var activeFamilyTokens = await dbContext.RefreshTokens
            .Where(refreshToken => refreshToken.FamilyId == familyId && refreshToken.RevokedAt == null)
            .ToListAsync(cancellationToken);

        if (activeFamilyTokens.Count == 0)
        {
            return;
        }

        var revokedAt = DateTime.UtcNow;

        foreach (var familyToken in activeFamilyTokens)
        {
            familyToken.RevokedAt = revokedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IssuedRefreshToken CreateRefreshToken(User user, Guid? familyId = null)
    {
        var token = tokenService.CreateRefreshToken();

        var entity = new RefreshToken
        {
            UserId = user.Id,
            FamilyId = familyId ?? Guid.NewGuid(),
            TokenHash = tokenService.HashRefreshToken(token),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays)
        };

        return new IssuedRefreshToken(entity, token);
    }

    private sealed record IssuedRefreshToken(RefreshToken Entity, string Token);
}
