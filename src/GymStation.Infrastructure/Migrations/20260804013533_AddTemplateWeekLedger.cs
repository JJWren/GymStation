using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateWeekLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClassTemplateWeeks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassTemplateWeeks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassTemplateWeeks_GymId_WeekStart_TemplateId",
                table: "ClassTemplateWeeks",
                columns: new[] { "GymId", "WeekStart", "TemplateId" },
                unique: true);

            // Claim every already-materialized template-week so existing calendars
            // don't re-mint. Weeks key on Sunday (EXTRACT(DOW) is 0 on Sunday, matching
            // Weeks.WeekOf). A previously MOVED occurrence claims the week it now sits
            // in — its origin week stays unclaimed, which at worst re-mints a slot that
            // was vacated before this fix shipped (the same refill that already
            // happened pre-ledger; gen_random_uuid is core PostgreSQL 13+).
            migrationBuilder.Sql("""
                INSERT INTO "ClassTemplateWeeks" ("Id", "GymId", "TemplateId", "WeekStart")
                SELECT gen_random_uuid(), s."GymId", s."TemplateId", (s."Date" - (EXTRACT(DOW FROM s."Date"))::int)
                FROM "ClassSessions" s
                WHERE s."TemplateId" IS NOT NULL
                GROUP BY s."GymId", s."TemplateId", (s."Date" - (EXTRACT(DOW FROM s."Date"))::int);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassTemplateWeeks");
        }
    }
}
