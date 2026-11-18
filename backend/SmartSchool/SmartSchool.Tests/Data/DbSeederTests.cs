using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSchool.API.Data;
using SmartSchool.API.Models;
using SmartSchool.API.Options;

namespace SmartSchool.Tests.Data;

[TestFixture]
public class DbSeederTests
{
    private const string AdminEmail = "admin@smartschool.dev";
    private const string AdminPassword = "correct-horse-battery";
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private ApplicationDbContext _dbContext = null!;
    private PasswordHasher<User> _passwordHasher = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"db-seeder-tests-{Guid.NewGuid()}")
                .Options);
        _passwordHasher = new PasswordHasher<User>();
    }

    [TearDown]
    public void TearDown() => _dbContext.Dispose();

    [Test]
    public async Task SeedAsync_OnEmptyDatabase_CreatesAllRolesAsActive()
    {
        await CreateSeeder().SeedAsync();

        var roles = await _dbContext.Roles.ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(roles.Select(role => role.Name), Is.EquivalentTo(RoleNames.All));
            Assert.That(roles, Has.All.Matches<Role>(role => role.IsActive));
        });
    }

    [Test]
    public async Task SeedAsync_OnEmptyDatabase_CreatesPlatformAdminWithPlatformWideAssignment()
    {
        await CreateSeeder().SeedAsync();

        var admin = await _dbContext.Users.SingleAsync();
        var assignment = await _dbContext.RoleAssignments.Include(a => a.Role).SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(admin.Email, Is.EqualTo(AdminEmail));
            Assert.That(
                _passwordHasher.VerifyHashedPassword(admin, admin.PasswordHash, AdminPassword),
                Is.Not.EqualTo(PasswordVerificationResult.Failed));
            Assert.That(assignment.UserId, Is.EqualTo(admin.Id));
            Assert.That(assignment.Role.Name, Is.EqualTo(RoleNames.PlatformAdmin));
            Assert.That(assignment.InstitutionId, Is.Null);
            Assert.That(assignment.ValidFrom, Is.EqualTo(DateOnly.FromDateTime(Now.UtcDateTime)));
            Assert.That(assignment.ValidTo, Is.Null);
        });
    }

    [Test]
    public async Task SeedAsync_RunTwice_IsIdempotent()
    {
        await CreateSeeder().SeedAsync();
        await CreateSeeder().SeedAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await _dbContext.Roles.CountAsync(), Is.EqualTo(RoleNames.All.Count));
            Assert.That(await _dbContext.Users.CountAsync(), Is.EqualTo(1));
            Assert.That(await _dbContext.RoleAssignments.CountAsync(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SeedAsync_WithExistingAdmin_DoesNotOverwritePassword()
    {
        var existing = new User { Email = AdminEmail, PasswordHash = string.Empty };
        existing.PasswordHash = _passwordHasher.HashPassword(existing, "an-older-password");
        _dbContext.Users.Add(existing);
        await _dbContext.SaveChangesAsync();

        await CreateSeeder().SeedAsync();

        var admin = await _dbContext.Users.SingleAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(
                _passwordHasher.VerifyHashedPassword(admin, admin.PasswordHash, "an-older-password"),
                Is.Not.EqualTo(PasswordVerificationResult.Failed));
            Assert.That(await _dbContext.RoleAssignments.CountAsync(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SeedAsync_WhenAdminAssignmentWasEnded_GrantsANewOpenAssignment()
    {
        await CreateSeeder().SeedAsync();
        var ended = await _dbContext.RoleAssignments.SingleAsync();
        ended.ValidTo = DateOnly.FromDateTime(Now.UtcDateTime);
        await _dbContext.SaveChangesAsync();

        await CreateSeeder().SeedAsync();

        var assignments = await _dbContext.RoleAssignments.ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(assignments, Has.Count.EqualTo(2));
            Assert.That(assignments.Count(a => a.ValidTo == null), Is.EqualTo(1));
        });
    }

    [TestCase("", AdminPassword)]
    [TestCase("   ", AdminPassword)]
    [TestCase(AdminEmail, "")]
    [TestCase(AdminEmail, "too-short")]
    public void SeedAsync_WithInvalidSettings_ThrowsAndWritesNothing(string email, string password)
    {
        Assert.ThrowsAsync<InvalidOperationException>(() => CreateSeeder(email, password).SeedAsync());
        Assert.That(_dbContext.Roles.Count(), Is.Zero);
    }

    private DbSeeder CreateSeeder(string email = AdminEmail, string password = AdminPassword) =>
        new(
            _dbContext,
            _passwordHasher,
            Options.Create(new SeedSettings
            {
                PlatformAdmin = new SeedSettings.PlatformAdminSeed
                {
                    Email = email,
                    Password = password
                }
            }),
            new FixedTimeProvider(Now),
            NullLogger<DbSeeder>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
