using System.ComponentModel.DataAnnotations;
using System.Reflection;
using SmartSchool.API.Contracts.Auth;

namespace SmartSchool.Tests.Contracts;

[TestFixture]
public class AuthRequestValidationTests
{
    [Test]
    public void LoginRequest_WithValidCredentials_HasNoValidationErrors()
    {
        var request = new LoginRequest("student@example.com", "Password123!");

        Assert.That(Validate(request), Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-an-email")]
    [TestCase("missing-at-sign.example.com")]
    [TestCase("@example.com")]
    [TestCase("student@")]
    public void LoginRequest_WithInvalidIdentifier_FailsValidation(string identifier)
    {
        AssertInvalid(
            new LoginRequest(identifier, "Password123!"),
            nameof(LoginRequest.Identifier));
    }

    [Test]
    public void LoginRequest_WithNullIdentifier_FailsValidation()
    {
        AssertInvalid(
            new LoginRequest(null!, "Password123!"),
            nameof(LoginRequest.Identifier));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void LoginRequest_WithEmptyPassword_FailsValidation(string password)
    {
        AssertInvalid(
            new LoginRequest("student@example.com", password),
            nameof(LoginRequest.Password));
    }

    [Test]
    public void LoginRequest_WithNullPassword_FailsValidation()
    {
        AssertInvalid(
            new LoginRequest("student@example.com", null!),
            nameof(LoginRequest.Password));
    }

    [Test]
    public void ChangePasswordRequest_WithValidPasswords_HasNoValidationErrors()
    {
        var request = new ChangePasswordRequest("OldPassword123!", "NewPassword123!");

        Assert.That(Validate(request), Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ChangePasswordRequest_WithEmptyCurrentPassword_FailsValidation(string currentPassword)
    {
        AssertInvalid(
            new ChangePasswordRequest(currentPassword, "NewPassword123!"),
            nameof(ChangePasswordRequest.CurrentPassword));
    }

    [Test]
    public void ChangePasswordRequest_WithNullCurrentPassword_FailsValidation()
    {
        AssertInvalid(
            new ChangePasswordRequest(null!, "NewPassword123!"),
            nameof(ChangePasswordRequest.CurrentPassword));
    }

    [TestCase("")]
    [TestCase("short")]
    [TestCase("12345678901")]
    [TestCase("   a   ")]
    public void ChangePasswordRequest_WithNewPasswordShorterThan12Characters_FailsValidation(string newPassword)
    {
        AssertInvalid(
            new ChangePasswordRequest("OldPassword123!", newPassword),
            nameof(ChangePasswordRequest.NewPassword));
    }

    [Test]
    public void ChangePasswordRequest_WithNullNewPassword_FailsValidation()
    {
        AssertInvalid(
            new ChangePasswordRequest("OldPassword123!", null!),
            nameof(ChangePasswordRequest.NewPassword));
    }

    [Test]
    public void ChangePasswordRequest_WithNewPasswordOfExactly12Characters_HasNoValidationErrors()
    {
        var request = new ChangePasswordRequest("OldPassword123!", "123456789012");

        Assert.That(Validate(request), Is.Empty);
    }

    [Test]
    public void RefreshTokenRequest_WithToken_HasNoValidationErrors()
    {
        var request = new RefreshTokenRequest("some-refresh-token");

        Assert.That(Validate(request), Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void RefreshTokenRequest_WithEmptyToken_FailsValidation(string refreshToken)
    {
        AssertInvalid(
            new RefreshTokenRequest(refreshToken),
            nameof(RefreshTokenRequest.RefreshToken));
    }

    [Test]
    public void RefreshTokenRequest_WithNullToken_FailsValidation()
    {
        AssertInvalid(
            new RefreshTokenRequest(null!),
            nameof(RefreshTokenRequest.RefreshToken));
    }

    // For record types MVC reads validation metadata from the constructor parameters, not the
    // generated properties, so Validator.TryValidateObject (which only inspects properties) does
    // not apply here. Emulate MVC by validating each constructor parameter's value against the
    // attributes declared on that parameter.
    private static IReadOnlyList<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        var requestType = request.GetType();

        foreach (var parameter in requestType.GetConstructors().Single().GetParameters())
        {
            var property = requestType.GetProperty(parameter.Name!);
            Assert.That(property, Is.Not.Null, $"Expected a property named '{parameter.Name}'.");

            var validationContext = new ValidationContext(request)
            {
                MemberName = parameter.Name
            };

            Validator.TryValidateValue(
                property!.GetValue(request),
                validationContext,
                results,
                parameter.GetCustomAttributes<ValidationAttribute>());
        }

        return results;
    }

    private static void AssertInvalid(object request, string expectedMemberName)
    {
        var invalidMembers = Validate(request)
            .SelectMany(result => result.MemberNames)
            .ToList();

        Assert.That(invalidMembers, Does.Contain(expectedMemberName));
    }
}
