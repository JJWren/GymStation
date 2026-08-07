using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankSoftDeleteAndRetire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Retired",
                table: "Ranks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByPersonId",
                table: "RankAwards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedUtc",
                table: "RankAwards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111201"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111202"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111203"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111204"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111205"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111206"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111207"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111208"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111300"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111301"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111302"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111303"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111304"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111305"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111306"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111307"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111308"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111309"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111310"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111311"),
                column: "Retired",
                value: false);

            migrationBuilder.UpdateData(
                table: "Ranks",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111312"),
                column: "Retired",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Retired",
                table: "Ranks");

            migrationBuilder.DropColumn(
                name: "DeletedByPersonId",
                table: "RankAwards");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "RankAwards");
        }
    }
}
