using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItemPriceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemPriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketHashName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CapturedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MinPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MedianPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MeanPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPriceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemPriceSnapshots_MarketHashName_Currency_CapturedDate",
                table: "ItemPriceSnapshots",
                columns: new[] { "MarketHashName", "Currency", "CapturedDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemPriceSnapshots");
        }
    }
}
