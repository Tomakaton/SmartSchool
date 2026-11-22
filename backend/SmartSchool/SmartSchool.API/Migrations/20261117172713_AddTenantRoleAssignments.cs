using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartSchool.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantRoleAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_RoleAssignments",
                table: "RoleAssignments");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ValidFrom",
                table: "RoleAssignments",
                type: "date",
                nullable: true);

            // Carry the original assignment date across; the sign-up row is the honest start of
            // the role. New rows always set ValidFrom explicitly.
            migrationBuilder.Sql(
                "UPDATE \"RoleAssignments\" SET \"ValidFrom\" = CAST(\"AssignedAt\" AS date);");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "ValidFrom",
                table: "RoleAssignments",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ValidTo",
                table: "RoleAssignments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InstitutionId",
                table: "RoleAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "RoleAssignments");

            // Postgres populates existing rows with generated identity values when an identity
            // column is added, so no zero default is used here.
            migrationBuilder.AddColumn<long>(
                name: "Id",
                table: "RoleAssignments",
                type: "bigint",
                nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "PK_RoleAssignments",
                table: "RoleAssignments",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_InstitutionId",
                table: "RoleAssignments",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_UserId_InstitutionId",
                table: "RoleAssignments",
                columns: new[] { "UserId", "InstitutionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_UserId_RoleId_InstitutionId",
                table: "RoleAssignments",
                columns: new[] { "UserId", "RoleId", "InstitutionId" },
                unique: true,
                filter: "\"InstitutionId\" IS NOT NULL AND \"ValidTo\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_UserId_RoleId_Platform",
                table: "RoleAssignments",
                columns: new[] { "UserId", "RoleId" },
                unique: true,
                filter: "\"InstitutionId\" IS NULL AND \"ValidTo\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RoleAssignments_Institutions_InstitutionId",
                table: "RoleAssignments",
                column: "InstitutionId",
                principalTable: "Institutions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoleAssignments_Institutions_InstitutionId",
                table: "RoleAssignments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RoleAssignments",
                table: "RoleAssignments");

            migrationBuilder.DropIndex(
                name: "IX_RoleAssignments_InstitutionId",
                table: "RoleAssignments");

            migrationBuilder.DropIndex(
                name: "IX_RoleAssignments_UserId_InstitutionId",
                table: "RoleAssignments");

            migrationBuilder.DropIndex(
                name: "IX_RoleAssignments_UserId_RoleId_InstitutionId",
                table: "RoleAssignments");

            migrationBuilder.DropIndex(
                name: "IX_RoleAssignments_UserId_RoleId_Platform",
                table: "RoleAssignments");

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "RoleAssignments",
                type: "timestamp with time zone",
                nullable: true,
                defaultValueSql: "now()");

            // Restore the sign-up timestamp from the closest surviving date before it is dropped.
            migrationBuilder.Sql(
                "UPDATE \"RoleAssignments\" SET \"AssignedAt\" = \"ValidFrom\"::timestamptz;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "AssignedAt",
                table: "RoleAssignments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Id",
                table: "RoleAssignments");

            migrationBuilder.DropColumn(
                name: "InstitutionId",
                table: "RoleAssignments");

            migrationBuilder.DropColumn(
                name: "ValidFrom",
                table: "RoleAssignments");

            migrationBuilder.DropColumn(
                name: "ValidTo",
                table: "RoleAssignments");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RoleAssignments",
                table: "RoleAssignments",
                columns: new[] { "UserId", "RoleId" });
        }
    }
}
