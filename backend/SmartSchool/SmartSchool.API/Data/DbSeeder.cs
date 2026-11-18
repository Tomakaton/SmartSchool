using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartSchool.API.Models;
using SmartSchool.API.Options;

namespace SmartSchool.API.Data;

/// <summary>
/// Seeds reference data (roles) and the first platform admin. Runs only when explicitly requested
/// (<c>dotnet run -- --seed</c>) rather than through <c>HasData</c>, so seeding never happens as a
/// side effect of a migration. Every step is idempotent: running it twice changes nothing.
/// </summary>
public class DbSeeder(
    ApplicationDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    IOptions<SeedSettings> seedSettings,
    TimeProvider timeProvider,
    ILogger<DbSeeder> logger)
{
    /// <summary>Command-line switch that runs the seeder instead of starting the web host.</summary>
    public const string CommandLineFlag = "--seed";

    /// <summary>Minimum length for the seeded admin password, matching the documented policy.</summary>
    public const int MinimumPasswordLength = 12;

    private readonly SeedSettings.PlatformAdminSeed _admin = seedSettings.Value.PlatformAdmin;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        ValidateSettings();

        await SeedRolesAsync(cancellationToken);
        await SeedPlatformAdminAsync(cancellationToken);
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_admin.Email))
        {
            throw new InvalidOperationException(
                $"'{SeedSettings.SectionName}:PlatformAdmin:Email' must be configured to seed.");
        }

        if (_admin.Password.Length < MinimumPasswordLength)
        {
            throw new InvalidOperationException(
                $"'{SeedSettings.SectionName}:PlatformAdmin:Password' must be at least " +
                $"{MinimumPasswordLength} characters long.");
        }
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        var existing = await dbContext.Roles
            .Select(role => role.Name)
            .ToListAsync(cancellationToken);

        var missing = RoleNames.All.Except(existing).ToList();

        foreach (var name in missing)
        {
            dbContext.Roles.Add(new Role { Name = name, IsActive = true });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Added} role(s); {Existing} already present.",
            missing.Count,
            RoleNames.All.Count - missing.Count);
    }

    private async Task SeedPlatformAdminAsync(CancellationToken cancellationToken)
    {
        var email = _admin.Email.Trim().ToLowerInvariant();
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var user = await dbContext.Users
            .FirstOrDefaultAsync(user => user.Email.ToLower() == email, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Email = email,
                PasswordHash = string.Empty,
                IsActivated = true,
                DateCreated = timeProvider.GetUtcNow().UtcDateTime
            };
            user.PasswordHash = passwordHasher.HashPassword(user, _admin.Password);

            dbContext.Users.Add(user);
            logger.LogInformation("Created platform admin {Email}.", email);
        }

        var platformAdminRole = await dbContext.Roles
            .SingleAsync(role => role.Name == RoleNames.PlatformAdmin, cancellationToken);

        var hasOpenAssignment = await dbContext.RoleAssignments.AnyAsync(
            assignment => assignment.UserId == user.Id
                && assignment.RoleId == platformAdminRole.Id
                && assignment.InstitutionId == null
                && assignment.ValidTo == null,
            cancellationToken);

        if (!hasOpenAssignment)
        {
            dbContext.RoleAssignments.Add(new RoleAssignment
            {
                UserId = user.Id,
                RoleId = platformAdminRole.Id,
                InstitutionId = null,
                ValidFrom = today
            });
            logger.LogInformation("Granted {Role} to {Email}.", RoleNames.PlatformAdmin, email);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
