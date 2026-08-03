using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLandingContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SocialFacebook",
                table: "GymSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialInstagram",
                table: "GymSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialYouTube",
                table: "GymSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaglineInstructors",
                table: "GymSettings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "INSTRUCTORS");

            migrationBuilder.AddColumn<string>(
                name: "TaglineSchedule",
                table: "GymSettings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "SCHEDULE");

            migrationBuilder.AddColumn<string>(
                name: "TaglineVisit",
                table: "GymSettings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "VISIT");

            migrationBuilder.AddColumn<string>(
                name: "VisitAddress",
                table: "GymSettings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitEmail",
                table: "GymSettings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitPhone",
                table: "GymSettings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SocialFacebook",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "SocialInstagram",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "SocialYouTube",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "TaglineInstructors",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "TaglineSchedule",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "TaglineVisit",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "VisitAddress",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "VisitEmail",
                table: "GymSettings");

            migrationBuilder.DropColumn(
                name: "VisitPhone",
                table: "GymSettings");
        }
    }
}
