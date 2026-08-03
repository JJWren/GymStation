using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LastMaterializedMonth",
                table: "RecurringExpenses",
                type: "date",
                nullable: true);

            // Backfill from already-materialized rows: without this, a live gym's
            // recurrings would all re-materialize this month if the owner deletes a
            // row right after upgrading (the exact bug the high-water mark prevents).
            // Gym-scoped join: RecurringExpenseId carries no FK constraint, so a
            // corrupt/imported row must never bleed a mark across tenants.
            migrationBuilder.Sql("""
                UPDATE "RecurringExpenses" AS r
                SET "LastMaterializedMonth" = s.m
                FROM (
                    SELECT "RecurringExpenseId" AS id, "GymId" AS gym, date_trunc('month', MAX("SpentOn"))::date AS m
                    FROM "Expenses"
                    WHERE "RecurringExpenseId" IS NOT NULL
                    GROUP BY "RecurringExpenseId", "GymId"
                ) AS s
                WHERE s.id = r."Id" AND s.gym = r."GymId";
                """);

            migrationBuilder.CreateTable(
                name: "OtherIncomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtherIncomes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OtherIncomes_GymId_ReceivedOn",
                table: "OtherIncomes",
                columns: new[] { "GymId", "ReceivedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OtherIncomes");

            migrationBuilder.DropColumn(
                name: "LastMaterializedMonth",
                table: "RecurringExpenses");
        }
    }
}
