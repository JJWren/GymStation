using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Families",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MembershipPlanId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Families", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FamilyGuardians",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuardianUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    ActForWards = table.Column<bool>(type: "boolean", nullable: false),
                    ManageGuardians = table.Column<bool>(type: "boolean", nullable: false),
                    ManageMembers = table.Column<bool>(type: "boolean", nullable: false),
                    ViewBilling = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyGuardians", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyGuardians_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FamilyMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsWard = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyMembers_Families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "Families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyMembers_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGuardians_FamilyId",
                table: "FamilyGuardians",
                column: "FamilyId",
                unique: true,
                filter: "\"IsPrimary\"");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGuardians_FamilyId_GuardianUserId",
                table: "FamilyGuardians",
                columns: new[] { "FamilyId", "GuardianUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembers_FamilyId",
                table: "FamilyMembers",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembers_GymId_PersonId",
                table: "FamilyMembers",
                columns: new[] { "GymId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembers_PersonId",
                table: "FamilyMembers",
                column: "PersonId");

            // Absorb GuardianLinks (#89): one family per (gym, owner-guardian), where a
            // child's owner is its MIN guardian. All of an owner's children join their
            // family as wards; every other guardian of any of those children joins the
            // same family as a non-primary acting guardian. Current data is strictly
            // one-guardian-per-child, but shared-custody rows migrate coherently too.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE _owner AS
                SELECT "ChildPersonId", "GymId", MIN("GuardianUserId"::text)::uuid AS owner
                FROM "GuardianLinks"
                GROUP BY "ChildPersonId", "GymId";

                CREATE TEMP TABLE _fam AS
                SELECT gen_random_uuid() AS id, "GymId", owner
                FROM (SELECT DISTINCT "GymId", owner FROM _owner) d;

                INSERT INTO "Families" ("Id", "GymId", "Name", "MembershipPlanId")
                SELECT f.id, f."GymId",
                       UPPER(COALESCE(
                           (SELECT MAX(p."LastName") FROM _owner o
                            JOIN "Persons" p ON p."Id" = o."ChildPersonId"
                            WHERE o.owner = f.owner AND o."GymId" = f."GymId"), 'FAMILY')) || ' FAMILY',
                       NULL
                FROM _fam f;

                INSERT INTO "FamilyMembers" ("Id", "GymId", "FamilyId", "PersonId", "IsWard")
                SELECT gen_random_uuid(), o."GymId", f.id, o."ChildPersonId", TRUE
                FROM _owner o
                JOIN _fam f ON f.owner = o.owner AND f."GymId" = o."GymId";

                INSERT INTO "FamilyGuardians"
                    ("Id", "GymId", "FamilyId", "GuardianUserId", "IsPrimary",
                     "ActForWards", "ManageGuardians", "ManageMembers", "ViewBilling")
                SELECT gen_random_uuid(), x."GymId", x.fam, x.guser,
                       x.is_owner, TRUE, x.is_owner, x.is_owner, x.is_owner
                FROM (SELECT DISTINCT l."GymId", f.id AS fam, l."GuardianUserId" AS guser,
                             (l."GuardianUserId" = f.owner) AS is_owner
                      FROM "GuardianLinks" l
                      JOIN _owner o ON o."ChildPersonId" = l."ChildPersonId" AND o."GymId" = l."GymId"
                      JOIN _fam f ON f.owner = o.owner AND f."GymId" = o."GymId") x;

                DROP TABLE _fam;
                DROP TABLE _owner;
                """);

            migrationBuilder.DropTable(
                name: "GuardianLinks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuardianLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuardianUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuardianLinks_Persons_ChildPersonId",
                        column: x => x.ChildPersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianLinks_ChildPersonId",
                table: "GuardianLinks",
                column: "ChildPersonId");

            migrationBuilder.Sql("""
                INSERT INTO "GuardianLinks" ("Id", "GymId", "GuardianUserId", "ChildPersonId")
                SELECT gen_random_uuid(), g."GymId", g."GuardianUserId", m."PersonId"
                FROM "FamilyGuardians" g
                JOIN "FamilyMembers" m ON m."FamilyId" = g."FamilyId" AND m."IsWard"
                WHERE g."ActForWards";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_GuardianLinks_GuardianUserId_ChildPersonId",
                table: "GuardianLinks",
                columns: new[] { "GuardianUserId", "ChildPersonId" },
                unique: true);

            migrationBuilder.DropTable(
                name: "FamilyGuardians");

            migrationBuilder.DropTable(
                name: "FamilyMembers");

            migrationBuilder.DropTable(
                name: "Families");
        }
    }
}
