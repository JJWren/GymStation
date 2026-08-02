using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGymHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "CloseTime",
                table: "GymSettings",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(22, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "OpenTime",
                table: "GymSettings",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(6, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloseTime",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "OpenTime",
                table: "GymSettings");
        }
    }
}
