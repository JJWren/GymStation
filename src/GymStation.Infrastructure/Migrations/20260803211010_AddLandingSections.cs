using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLandingSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AboutText",
                table: "GymSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AboutTitle",
                table: "GymSettings",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "ABOUT");

            migrationBuilder.AddColumn<string>(
                name: "ProgramsIntro",
                table: "GymSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProgramsTitle",
                table: "GymSettings",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "PROGRAMS");

            migrationBuilder.AddColumn<string>(
                name: "SectionOrder",
                table: "GymSettings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "about,programs,schedule,instructors,stories,visit");

            migrationBuilder.AddColumn<string>(
                name: "StoriesImagePath",
                table: "GymSettings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoriesTitle",
                table: "GymSettings",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "SUCCESS STORIES");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AboutText",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "AboutTitle",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "ProgramsIntro",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "ProgramsTitle",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "SectionOrder",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "StoriesImagePath",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "StoriesTitle",
                table: "GymSettings");
        }
    }
}
