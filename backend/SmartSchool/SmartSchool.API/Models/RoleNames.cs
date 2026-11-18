namespace SmartSchool.API.Models;

/// <summary>
/// Canonical role names. Stored in <c>Roles.Name</c> and used on the wire, so they follow the API's
/// SCREAMING_CASE enum convention.
/// </summary>
public static class RoleNames
{
    /// <summary>Platform operator. Only valid on a platform-wide assignment (<c>InstitutionId = NULL</c>).</summary>
    public const string PlatformAdmin = "PLATFORM_ADMIN";

    public const string InstitutionAdmin = "INSTITUTION_ADMIN";
    public const string InstitutionManager = "INSTITUTION_MANAGER";
    public const string Teacher = "TEACHER";
    public const string Student = "STUDENT";
    public const string Parent = "PARENT";

    /// <summary>Every role the platform knows about, in seeding order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        PlatformAdmin,
        InstitutionAdmin,
        InstitutionManager,
        Teacher,
        Student,
        Parent
    ];
}
