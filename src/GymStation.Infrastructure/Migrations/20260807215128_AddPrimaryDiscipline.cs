using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrimaryDiscipline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryRankSystemId",
                table: "Persons",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_PrimaryRankSystemId",
                table: "Persons",
                column: "PrimaryRankSystemId");

            migrationBuilder.AddForeignKey(
                name: "FK_Persons_RankSystems_PrimaryRankSystemId",
                table: "Persons",
                column: "PrimaryRankSystemId",
                principalTable: "RankSystems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Persons_RankSystems_PrimaryRankSystemId",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_Persons_PrimaryRankSystemId",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "PrimaryRankSystemId",
                table: "Persons");
        }
    }
}
