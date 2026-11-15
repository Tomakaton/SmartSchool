namespace SmartSchool.API.Contracts.Auth;

public sealed record RefreshTokenResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);
