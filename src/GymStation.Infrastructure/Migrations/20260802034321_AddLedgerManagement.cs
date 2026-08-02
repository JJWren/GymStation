using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Payments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByPersonId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedUtc",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecurringExpenseId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_ExpenseCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_RecurringExpenseId_SpentOn",
                table: "Expenses",
                columns: new[] { "RecurringExpenseId", "SpentOn" },
                unique: true,
                filter: "\"RecurringExpenseId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_CategoryId",
                table: "RecurringExpenses",
                column: "CategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringExpenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_RecurringExpenseId_SpentOn",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedByPersonId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RecurringExpenseId",
                table: "Expenses");
        }
    }
}
