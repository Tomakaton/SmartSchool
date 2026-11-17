using System.ComponentModel.DataAnnotations;

namespace SmartSchool.API.Contracts.Auth;

public sealed record LoginRequest(
    [param: Required, EmailAddress] string Identifier,
    [param: Required] string Password);
