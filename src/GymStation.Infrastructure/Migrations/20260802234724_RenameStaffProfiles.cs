using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameStaffProfiles : Migration
    {
        // Hand-written rename: the scaffold produced DropTable + CreateTable, which
        // would wipe every live profile. Constraint names ride along so future
        // migrations derive them correctly by convention.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "InstructorProfiles",
                newName: "StaffProfiles");

            migrationBuilder.Sql("""ALTER TABLE "StaffProfiles" RENAME CONSTRAINT "PK_InstructorProfiles" TO "PK_StaffProfiles";""");
            migrationBuilder.Sql("""ALTER TABLE "StaffProfiles" RENAME CONSTRAINT "FK_InstructorProfiles_Persons_PersonId" TO "FK_StaffProfiles_Persons_PersonId";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "StaffProfiles" RENAME CONSTRAINT "FK_StaffProfiles_Persons_PersonId" TO "FK_InstructorProfiles_Persons_PersonId";""");
            migrationBuilder.Sql("""ALTER TABLE "StaffProfiles" RENAME CONSTRAINT "PK_StaffProfiles" TO "PK_InstructorProfiles";""");

            migrationBuilder.RenameTable(
                name: "StaffProfiles",
                newName: "InstructorProfiles");
        }
    }
}
