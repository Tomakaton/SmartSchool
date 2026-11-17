using System.ComponentModel.DataAnnotations;

namespace SmartSchool.API.Contracts.Auth;

public sealed record RefreshTokenRequest(
    [param: Required] string RefreshToken);
