using SmartSchool.API.Models;

namespace SmartSchool.API.Services;

/// <summary>
/// Creates signed access tokens and opaque refresh tokens, and hashes refresh tokens for storage.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Creates a signed JWT carrying the user's identity and security stamp (<c>sub</c>,
    /// <c>jti</c>, <c>iat</c>, <c>exp</c>, <c>stamp</c>). Authorization roles are never embedded.
    /// </summary>
    /// <returns>The serialized token and its UTC expiry.</returns>
    AccessTokenResult CreateAccessToken(User user);

    /// <summary>
    /// Creates a cryptographically random refresh token to hand back to the client.
    /// </summary>
    string CreateRefreshToken();

    /// <summary>
    /// Returns the hash that is persisted in place of the raw refresh token.
    /// </summary>
    string HashRefreshToken(string refreshToken);
}
