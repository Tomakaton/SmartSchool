using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.API.Contracts.Auth;
using SmartSchool.API.Contracts.Common;
using SmartSchool.API.Services;

namespace SmartSchool.API.Controllers;

/// <summary>Authentication endpoints: login, refresh-token rotation, and password changes.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Authenticates a user and returns an access/refresh token pair.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);

        if (!result.Succeeded)
        {
            return result.ErrorKind == ResultErrorKind.Forbidden
                ? StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error })
                : Unauthorized(new { message = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Exchanges a valid refresh token for a new token pair. The presented token is revoked.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshTokenResponse>> RefreshToken(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RefreshTokenAsync(request, cancellationToken);
        
        if (!result.Succeeded)
        {
            return Unauthorized(new { message = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Changes the authenticated user's password and revokes their active refresh tokens.
    /// </summary>
    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Invalid access token." });
        }

        var result = await authService.ChangePasswordAsync(userId, request, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.Error });
        }

        return NoContent();
    }
}
