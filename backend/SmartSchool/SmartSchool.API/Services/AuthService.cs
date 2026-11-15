using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartSchool.API.Contracts.Auth;
using SmartSchool.API.Contracts.Common;
using SmartSchool.API.Data;
using SmartSchool.API.Models;
using SmartSchool.API.Options;

namespace SmartSchool.API.Services;

public class AuthService(
    ApplicationDbContext dbContext,
    ITokenService tokenService,
    IPasswordHasher<User> passwordHasher,
    IOptions<JwtSettings> jwtSettings) : IAuthService
{
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;

    public async Task<Result<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Identifier.Trim();

        var user = await dbContext.Users
            .Include(user => user.RoleAssignments)
            .ThenInclude(roleAssignment => roleAssignment.Role)
            .FirstOrDefaultAsync(user => EF.Functions.ILike(user.Email, email), cancellationToken);

        if (user is null)
        {
            return Result<LoginResponse>.Failure("Invalid email or password.");
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        switch (verificationResult)
        {
            case PasswordVerificationResult.Failed:
                return Result<LoginResponse>.Failure("Invalid email or password.");
            case PasswordVerificationResult.SuccessRehashNeeded:
                user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
                break;
            case PasswordVerificationResult.Success:
                break;
            default:
                return Result<LoginResponse>.Failure("Invalid login attempt.");
        }

        var accessToken = CreateAccessToken(user);
        var refreshToken = CreateRefreshToken(user);

        dbContext.RefreshTokens.Add(refreshToken.Entity);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<LoginResponse>.Success(new LoginResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Token,
            refreshToken.Entity.ExpiresAt));
    }

    public async Task<Result<RefreshTokenResponse>> RefreshTokenAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);

        var storedToken = await dbContext.RefreshTokens
            .Include(refreshToken => refreshToken.User)
            .ThenInclude(user => user.RoleAssignments)
            .ThenInclude(roleAssignment => roleAssignment.Role)
            .FirstOrDefaultAsync(
                refreshToken => refreshToken.TokenHash == tokenHash,
                cancellationToken);

        if (storedToken is null
            || storedToken.RevokedAt is not null
            || storedToken.ExpiresAt <= DateTime.UtcNow)
        {
            return Result<RefreshTokenResponse>.Failure("Invalid or expired refresh token.");
        }

        var user = storedToken.User;
        var accessToken = CreateAccessToken(user);
        var refreshToken = CreateRefreshToken(user);

        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.ReplacedByToken = refreshToken.Entity;

        dbContext.RefreshTokens.Add(refreshToken.Entity);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RefreshTokenResponse>.Success(new RefreshTokenResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Token,
            refreshToken.Entity.ExpiresAt));
    }

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

    private AccessTokenResult CreateAccessToken(User user)
    {
        var roles = user.RoleAssignments
            .Where(roleAssignment => roleAssignment.Role.IsActive)
            .Select(roleAssignment => roleAssignment.Role.Name);

        return tokenService.CreateAccessToken(user, roles);
    }

    private IssuedRefreshToken CreateRefreshToken(User user)
    {
        var token = tokenService.CreateRefreshToken();

        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(token),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays)
        };

        return new IssuedRefreshToken(entity, token);
    }

    private sealed record IssuedRefreshToken(RefreshToken Entity, string Token);
}
