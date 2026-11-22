using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using SmartSchool.API.Data;

namespace SmartSchool.API.Services;

/// <summary>
/// Verifies on every authenticated request that the token's <c>stamp</c> claim still matches the
/// user's persisted <see cref="Models.User.SecurityStamp"/> and that the account remains active.
/// Account-wide security events (password change/reset, deactivation, platform-admin changes)
/// rotate the stamp so issued access tokens fail closed immediately instead of living out their TTL.
/// </summary>
public static class JwtSecurityStampValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var sub = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var tokenStamp = context.Principal?.FindFirstValue(TokenClaimTypes.SecurityStamp);

        if (!Guid.TryParse(sub, out var userId) || string.IsNullOrEmpty(tokenStamp))
        {
            context.Fail("Invalid access token claims.");
            return;
        }

        var dbContext = context.HttpContext.RequestServices
            .GetRequiredService<ApplicationDbContext>();

        var userState = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.IsActivated, user.SecurityStamp })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (userState is null || !userState.IsActivated)
        {
            context.Fail("User account is not active.");
            return;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(userState.SecurityStamp);
        var providedBytes = Encoding.UTF8.GetBytes(tokenStamp);

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
        {
            context.Fail("Security stamp mismatch.");
        }
    }
}
