using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.API.Data;
using SmartSchool.API.Models;

namespace SmartSchool.API.Controllers;

// Keep validation attributes on the constructor parameters ([param: ...]); see Contracts/Auth/LoginRequest.
public sealed record RegisterRequest(
    [param: Required, EmailAddress] string Email,
    [param: Required, MinLength(8)] string Password);

public sealed record RegisterResponse(Guid Id, string Email, DateTime DateCreated);

// Temporary endpoint used to create users until a proper user management flow exists.
// It is anonymous and unverified, so it must not ship to production as-is.
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class RegisterController(
    ApplicationDbContext dbContext,
    IPasswordHasher<User> passwordHasher) : ControllerBase
{
    /// <summary>Creates a user from an email and password. Temporary and anonymous.</summary>
    [HttpPost]
    public async Task<ActionResult<RegisterResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();

        var emailAlreadyExists = await dbContext.Users
            .AnyAsync(
                user => EF.Functions.ILike(user.Email, email),
                cancellationToken);

        if (emailAlreadyExists)
        {
            return Conflict(new { message = "A user with this email already exists." });
        }

        var user = new User
        {
            Email = email,
            PasswordHash = string.Empty,
            IsActivated = true,
            DateCreated = DateTime.UtcNow
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(Register),
            new RegisterResponse(user.Id, user.Email, user.DateCreated));
    }
}
