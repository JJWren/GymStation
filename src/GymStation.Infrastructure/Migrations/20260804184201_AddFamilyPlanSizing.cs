using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyPlanSizing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExtraAdultPrice",
                table: "MembershipPlans",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExtraKidPrice",
                table: "MembershipPlans",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "IncludedAdults",
                table: "MembershipPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IncludedKids",
                table: "MembershipPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FamilyAdults",
                table: "Charges",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FamilyExtraAmount",
                table: "Charges",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FamilyKids",
                table: "Charges",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraAdultPrice",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "ExtraKidPrice",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "IncludedAdults",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "IncludedKids",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "FamilyAdults",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "FamilyExtraAmount",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "FamilyKids",
                table: "Charges");
        }
    }
}
