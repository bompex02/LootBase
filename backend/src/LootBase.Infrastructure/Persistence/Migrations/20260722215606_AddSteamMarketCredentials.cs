using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamMarketCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SteamMarketCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    RefreshToken = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SteamMarketCredentials", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SteamMarketCredentials");
        }
    }
}
