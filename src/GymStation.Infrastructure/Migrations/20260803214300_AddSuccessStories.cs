using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSuccessStories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SuccessStories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    AttributedTo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuccessStories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SuccessStories_GymId_SortOrder",
                table: "SuccessStories",
                columns: new[] { "GymId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SuccessStories");
        }
    }
}
