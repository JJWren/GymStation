using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GymEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeInfo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PublishedByPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GymEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SelfReportedMinutes = table.Column<int>(type: "integer", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventRsvps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRsvps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventRsvps_GymEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "GymEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingRolls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartnerLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingRolls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingRolls_TrainingEntries_TrainingEntryId",
                        column: x => x.TrainingEntryId,
                        principalTable: "TrainingEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventRsvps_EventId_PersonId",
                table: "EventRsvps",
                columns: new[] { "EventId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GymEvents_GymId_StartsOn",
                table: "GymEvents",
                columns: new[] { "GymId", "StartsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingEntries_GymId_PersonId_EntryDate",
                table: "TrainingEntries",
                columns: new[] { "GymId", "PersonId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingRolls_TrainingEntryId",
                table: "TrainingRolls",
                column: "TrainingEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventRsvps");

            migrationBuilder.DropTable(
                name: "TrainingRolls");

            migrationBuilder.DropTable(
                name: "GymEvents");

            migrationBuilder.DropTable(
                name: "TrainingEntries");
        }
    }
}
