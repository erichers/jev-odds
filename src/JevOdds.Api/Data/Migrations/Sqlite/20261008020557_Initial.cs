using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JevOdds.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceBars",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Close = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceBars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueryRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Percent = table.Column<double>(type: "REAL", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    VolWindow = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    VolOverridePercent = table.Column<double>(type: "REAL", nullable: true),
                    Drift = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Spot = table.Column<double>(type: "REAL", nullable: false),
                    Sigma = table.Column<double>(type: "REAL", nullable: false),
                    AnalyticClose = table.Column<double>(type: "REAL", nullable: false),
                    AnalyticTouch = table.Column<double>(type: "REAL", nullable: false),
                    MonteCarloClose = table.Column<double>(type: "REAL", nullable: false),
                    MonteCarloTouch = table.Column<double>(type: "REAL", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TickerCaches",
                columns: table => new
                {
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TickerCaches", x => x.Ticker);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceBars_Ticker_Date",
                table: "PriceBars",
                columns: new[] { "Ticker", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueryRecords_CreatedAtUtc",
                table: "QueryRecords",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceBars");

            migrationBuilder.DropTable(
                name: "QueryRecords");

            migrationBuilder.DropTable(
                name: "TickerCaches");
        }
    }
}
