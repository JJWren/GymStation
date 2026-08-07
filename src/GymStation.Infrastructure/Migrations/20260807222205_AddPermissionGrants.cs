using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PermissionGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Capability = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermissionGrants_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PermissionGrants_GymId_PersonId_Capability",
                table: "PermissionGrants",
                columns: new[] { "GymId", "PersonId", "Capability" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermissionGrants_PersonId",
                table: "PermissionGrants",
                column: "PersonId");

            // Backfill: every existing Admin keeps working exactly as before the
            // permission system existed — the full capability set (1..10, the
            // GymCapability values). Owners are implicitly all-capable and get no
            // rows. Roles bit 4 = Admin (PersonRoles flags). gen_random_uuid()
            // is core PostgreSQL 13+.
            migrationBuilder.Sql("""
                INSERT INTO "PermissionGrants" ("Id", "GymId", "PersonId", "Capability")
                SELECT gen_random_uuid(), p."GymId", p."Id", c.capability
                FROM "Persons" p
                CROSS JOIN generate_series(1, 10) AS c(capability)
                WHERE (p."Roles" & 4) = 4 AND (p."Roles" & 8) = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PermissionGrants");
        }
    }
}
