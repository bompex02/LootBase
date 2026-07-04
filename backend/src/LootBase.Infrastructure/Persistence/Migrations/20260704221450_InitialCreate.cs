using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SteamId64 = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PersonaName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastInventoryRefreshAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    AssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MarketHashName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    IconUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Exterior = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Rarity = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    LastPricedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventorySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    TotalValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventorySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventorySnapshots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_UserId_AppId_AssetId",
                table: "InventoryItems",
                columns: new[] { "UserId", "AppId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventorySnapshots_UserId_AppId_CapturedAt",
                table: "InventorySnapshots",
                columns: new[] { "UserId", "AppId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_SteamId64",
                table: "Users",
                column: "SteamId64",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryItems");

            migrationBuilder.DropTable(
                name: "InventorySnapshots");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
