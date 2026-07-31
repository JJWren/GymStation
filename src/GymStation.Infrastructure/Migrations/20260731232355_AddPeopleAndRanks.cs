using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GymStation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeopleAndRanks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PortraitPath",
                table: "Persons",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InstructorProfiles",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bio = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExperienceSummary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PayRate = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    PayRateUnit = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstructorProfiles", x => x.PersonId);
                    table.ForeignKey(
                        name: "FK_InstructorProfiles_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RankSystems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsSeeded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankSystems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ranks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RankSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    MaxStripes = table.Column<int>(type: "integer", nullable: false),
                    BandColorHex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    BarColorHex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ranks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ranks_RankSystems_RankSystemId",
                        column: x => x.RankSystemId,
                        principalTable: "RankSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RankAwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GymId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stripes = table.Column<int>(type: "integer", nullable: false),
                    AwardedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    AwardedByPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelfReported = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RankAwards_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RankAwards_Ranks_RankId",
                        column: x => x.RankId,
                        principalTable: "Ranks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RankSystems",
                columns: new[] { "Id", "GymId", "IsSeeded", "Name" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111101"), null, true, "IBJJF Adult" },
                    { new Guid("11111111-1111-1111-1111-111111111102"), null, true, "IBJJF Kids" }
                });

            migrationBuilder.InsertData(
                table: "Ranks",
                columns: new[] { "Id", "BandColorHex", "BarColorHex", "MaxStripes", "Name", "Order", "RankSystemId" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111201"), "#E9E6DC", "#17181A", 4, "White", 1, new Guid("11111111-1111-1111-1111-111111111101") },
                    { new Guid("11111111-1111-1111-1111-111111111202"), "#2456A6", "#17181A", 4, "Blue", 2, new Guid("11111111-1111-1111-1111-111111111101") },
                    { new Guid("11111111-1111-1111-1111-111111111203"), "#5C3D93", "#17181A", 4, "Purple", 3, new Guid("11111111-1111-1111-1111-111111111101") },
                    { new Guid("11111111-1111-1111-1111-111111111204"), "#7A5230", "#17181A", 4, "Brown", 4, new Guid("11111111-1111-1111-1111-111111111101") },
                    { new Guid("11111111-1111-1111-1111-111111111205"), "#17181A", "#A31D26", 6, "Black", 5, new Guid("11111111-1111-1111-1111-111111111101") },
                    { new Guid("11111111-1111-1111-1111-111111111301"), "#B8BDC6", "#17181A", 4, "Grey-White", 1, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111302"), "#9BA1AB", "#17181A", 4, "Grey", 2, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111303"), "#7E848E", "#17181A", 4, "Grey-Black", 3, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111304"), "#F0D275", "#17181A", 4, "Yellow-White", 4, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111305"), "#E8C13A", "#17181A", 4, "Yellow", 5, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111306"), "#C7A32E", "#17181A", 4, "Yellow-Black", 6, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111307"), "#EBA06A", "#17181A", 4, "Orange-White", 7, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111308"), "#E07B39", "#17181A", 4, "Orange", 8, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111309"), "#BF6830", "#17181A", 4, "Orange-Black", 9, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111310"), "#74B18C", "#17181A", 4, "Green-White", 10, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111311"), "#3E8E5A", "#17181A", 4, "Green", 11, new Guid("11111111-1111-1111-1111-111111111102") },
                    { new Guid("11111111-1111-1111-1111-111111111312"), "#33774B", "#17181A", 4, "Green-Black", 12, new Guid("11111111-1111-1111-1111-111111111102") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankAwards_GymId_PersonId_AwardedOn",
                table: "RankAwards",
                columns: new[] { "GymId", "PersonId", "AwardedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_RankAwards_PersonId",
                table: "RankAwards",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_RankAwards_RankId",
                table: "RankAwards",
                column: "RankId");

            migrationBuilder.CreateIndex(
                name: "IX_Ranks_RankSystemId_Order",
                table: "Ranks",
                columns: new[] { "RankSystemId", "Order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstructorProfiles");

            migrationBuilder.DropTable(
                name: "RankAwards");

            migrationBuilder.DropTable(
                name: "Ranks");

            migrationBuilder.DropTable(
                name: "RankSystems");

            migrationBuilder.DropColumn(
                name: "PortraitPath",
                table: "Persons");
        }
    }
}
