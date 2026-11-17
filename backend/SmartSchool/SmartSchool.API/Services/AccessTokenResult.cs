namespace SmartSchool.API.Services;

/// <summary>A serialized access token and its UTC expiry.</summary>
public sealed record AccessTokenResult(string Value, DateTime ExpiresAt);
