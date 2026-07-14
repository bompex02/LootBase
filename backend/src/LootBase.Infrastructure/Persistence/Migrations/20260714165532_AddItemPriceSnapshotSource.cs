using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItemPriceSnapshotSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ItemPriceSnapshots",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "skinport");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "ItemPriceSnapshots");
        }
    }
}
