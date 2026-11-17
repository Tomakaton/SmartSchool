using System.ComponentModel.DataAnnotations;

namespace SmartSchool.API.Contracts.Auth;

public sealed record ChangePasswordRequest(
    [param: Required] string CurrentPassword,
    [param: Required, MinLength(8)] string NewPassword);
