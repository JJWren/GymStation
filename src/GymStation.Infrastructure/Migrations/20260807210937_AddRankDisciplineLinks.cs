using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankDisciplineLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RankSystemProgramLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    GymProgramId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankSystemProgramLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RankSystemProgramLinks_GymPrograms_GymProgramId",
                        column: x => x.GymProgramId,
                        principalTable: "GymPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RankSystemProgramLinks_RankSystems_RankSystemId",
                        column: x => x.RankSystemId,
                        principalTable: "RankSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankSystemProgramLinks_GymId_RankSystemId",
                table: "RankSystemProgramLinks",
                columns: new[] { "GymId", "RankSystemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RankSystemProgramLinks_GymProgramId",
                table: "RankSystemProgramLinks",
                column: "GymProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_RankSystemProgramLinks_RankSystemId",
                table: "RankSystemProgramLinks",
                column: "RankSystemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankSystemProgramLinks");
        }
    }
}
