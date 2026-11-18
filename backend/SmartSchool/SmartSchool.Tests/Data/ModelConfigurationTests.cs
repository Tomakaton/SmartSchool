using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartSchool.API.Data;
using SmartSchool.API.Models;

namespace SmartSchool.Tests.Data;

/// <summary>
/// Checks EF model configuration that InMemory tests would otherwise not exercise: value
/// conversions and the relational check constraints that the migrations emit.
/// </summary>
[TestFixture]
public class ModelConfigurationTests
{
    private ApplicationDbContext _dbContext = null!;

    [SetUp]
    public void SetUp() =>
        _dbContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"model-configuration-tests-{Guid.NewGuid()}")
                .Options);

    [TearDown]
    public void TearDown() => _dbContext.Dispose();

    [TestCase(InstitutionStatus.Active, "ACTIVE")]
    [TestCase(InstitutionStatus.Suspended, "SUSPENDED")]
    [TestCase(InstitutionStatus.Archived, "ARCHIVED")]
    public void InstitutionStatus_IsStoredAsScreamingCaseWireName(InstitutionStatus status, string expected)
    {
        var converter = _dbContext.Model
            .FindEntityType(typeof(Institution))!
            .FindProperty(nameof(Institution.Status))!
            .GetValueConverter()!;

        Assert.Multiple(() =>
        {
            Assert.That(converter.ConvertToProvider(status), Is.EqualTo(expected));
            Assert.That(converter.ConvertFromProvider(expected), Is.EqualTo(status));
        });
    }

    [Test]
    public void InstitutionStatus_ReadsLegacyPascalCaseValues()
    {
        var converter = _dbContext.Model
            .FindEntityType(typeof(Institution))!
            .FindProperty(nameof(Institution.Status))!
            .GetValueConverter()!;

        Assert.That(converter.ConvertFromProvider("Suspended"), Is.EqualTo(InstitutionStatus.Suspended));
    }

    [TestCase(typeof(Institution), "CK_Institutions_Status")]
    [TestCase(typeof(RoleAssignment), "CK_RoleAssignments_ValidDates")]
    public void CheckConstraint_IsConfigured(Type entityType, string constraintName)
    {
        // Check constraints are design-time metadata; the runtime model does not keep them.
        var constraints = _dbContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(entityType)!
            .GetCheckConstraints()
            .Select(constraint => constraint.ModelName);

        Assert.That(constraints, Does.Contain(constraintName));
    }

    [Test]
    public async Task RoleAssignment_CanBeEndedInPlace()
    {
        var role = new Role { Id = 1, Name = "TEACHER", IsActive = true };
        var user = new User { Email = "teacher@example.com", PasswordHash = "hash" };
        var assignment = new RoleAssignment
        {
            UserId = user.Id,
            User = user,
            RoleId = role.Id,
            Role = role,
            ValidFrom = new DateOnly(2026, 9, 1)
        };

        _dbContext.AddRange(role, user, assignment);
        await _dbContext.SaveChangesAsync();

        assignment.ValidTo = new DateOnly(2026, 11, 18);
        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.RoleAssignments.SingleAsync();

        Assert.That(stored.ValidTo, Is.EqualTo(new DateOnly(2026, 11, 18)));
    }

    [Test]
    public void User_DefaultsToUnactivatedWithUniqueSecurityStamp()
    {
        var first = new User { Email = "a@example.com", PasswordHash = string.Empty };
        var second = new User { Email = "b@example.com", PasswordHash = string.Empty };
        var isActivatedDefault = _dbContext.Model
            .FindEntityType(typeof(User))!
            .FindProperty(nameof(User.IsActivated))!
            .GetDefaultValue();

        Assert.Multiple(() =>
        {
            Assert.That(first.IsActivated, Is.False);
            Assert.That(isActivatedDefault, Is.EqualTo(false));
            Assert.That(first.SecurityStamp, Has.Length.EqualTo(32));
            Assert.That(second.SecurityStamp, Is.Not.EqualTo(first.SecurityStamp));
        });
    }
}
