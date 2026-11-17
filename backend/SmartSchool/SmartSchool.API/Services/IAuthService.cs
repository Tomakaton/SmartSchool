using SmartSchool.API.Contracts.Auth;
using SmartSchool.API.Contracts.Common;

namespace SmartSchool.API.Services;

/// <summary>
/// Handles credential verification, token issuance and rotation, and password changes.
/// Expected failures are returned as failed <see cref="Result"/> values, not exceptions.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Verifies the supplied credentials and issues a new access/refresh token pair.
    /// </summary>
    /// <returns>
    /// A failed result with a generic error when the email or password is invalid. The message is
    /// intentionally identical in both cases so callers cannot probe which emails are registered.
    /// </returns>
    Task<Result<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a valid refresh token for a new token pair, revoking the presented token and
    /// recording the replacement.
    /// </summary>
    /// <returns>A failed result when the token is unknown, already revoked, or expired.</returns>
    Task<Result<RefreshTokenResponse>> RefreshTokenAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the user's password and revokes all of their non-revoked refresh tokens.
    /// </summary>
    /// <returns>
    /// A failed result when the user does not exist or <c>CurrentPassword</c> is incorrect.
    /// </returns>
    Task<Result> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
