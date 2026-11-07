using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceTrendModelArtifact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceTrendModelArtifacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    TrainedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceTrendModelArtifacts", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceTrendModelArtifacts");
        }
    }
}
