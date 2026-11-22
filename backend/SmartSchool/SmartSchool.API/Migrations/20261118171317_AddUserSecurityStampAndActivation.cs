using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.API.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSecurityStampAndActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActivated",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Accounts created before activation existed already hold a password and were usable,
            // while future provisioned rows default to false until magic-link consumption.
            migrationBuilder.Sql(
                "UPDATE \"Users\" SET \"IsActivated\" = TRUE;");

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Populate each existing account with its own random 128-bit stamp rather than a shared
            // empty default, then enforce NOT NULL with no column default.
            migrationBuilder.Sql(
                "UPDATE \"Users\" SET \"SecurityStamp\" = replace(gen_random_uuid()::text, '-', '');");

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActivated",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");
        }
    }
}
