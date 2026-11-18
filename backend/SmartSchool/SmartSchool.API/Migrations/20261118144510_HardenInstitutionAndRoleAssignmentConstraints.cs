using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.API.Migrations
{
    /// <inheritdoc />
    public partial class HardenInstitutionAndRoleAssignmentConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_RoleAssignments_ValidDates",
                table: "RoleAssignments",
                sql: "\"ValidTo\" IS NULL OR \"ValidTo\" >= \"ValidFrom\"");

            // Rows written before this migration hold the C# member name ("Active"); convert them
            // to the wire name before the constraint is enforced.
            migrationBuilder.Sql(
                "UPDATE \"Institutions\" SET \"Status\" = upper(\"Status\");");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Institutions_Status",
                table: "Institutions",
                sql: "\"Status\" IN ('ACTIVE', 'SUSPENDED', 'ARCHIVED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RoleAssignments_ValidDates",
                table: "RoleAssignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Institutions_Status",
                table: "Institutions");

            // Restore the PascalCase member names the previous model wrote.
            migrationBuilder.Sql(
                "UPDATE \"Institutions\" SET \"Status\" = initcap(\"Status\");");
        }
    }
}
