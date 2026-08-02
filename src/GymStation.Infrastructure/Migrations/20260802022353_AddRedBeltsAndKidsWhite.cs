using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRedBeltsAndKidsWhite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Ranks",
                columns: new[] { "Id", "BandColorHex", "BarColorHex", "MaxStripes", "Name", "Order", "RankSystemId" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111206"), "#521A1E", "#17181A", 0, "Red & Black", 6, new Guid("11111111-1111-1111-1111-111111111101") },
                    { new Guid("11111111-1111-1111-1111-111111111207"), "#A31D26", "#E9E6DC", 0, "Red & White", 7, new Guid("11111111-1111-1111-1111-111111111101") },
                    { new Guid("11111111-1111-1111-1111-111111111208"), "#A31D26", "#7A1218", 0, "Red", 8, new Guid("11111111-1111-1111-1111-111111111101") },
                    { new Guid("11111111-1111-1111-1111-111111111300"), "#E9E6DC", "#17181A", 4, "White", 0, new Guid("11111111-1111-1111-1111-111111111102") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111206"));

            migrationBuilder.DeleteData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111207"));

            migrationBuilder.DeleteData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111208"));

            migrationBuilder.DeleteData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111300"));
        }
    }
}
