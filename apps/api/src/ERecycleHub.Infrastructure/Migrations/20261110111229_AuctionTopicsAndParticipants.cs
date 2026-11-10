using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERecycleHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuctionTopicsAndParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "DeviceTokens",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "DeviceTokens",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TopicName",
                table: "Auctions",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true,
                computedColumnSql: "'auction_' + CAST([Id] AS nvarchar(20))",
                stored: true);

            migrationBuilder.CreateTable(
                name: "AuctionParticipants",
                columns: table => new
                {
                    AuctionId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionParticipants", x => new { x.AuctionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_AuctionParticipants_Auctions_AuctionId",
                        column: x => x.AuctionId,
                        principalTable: "Auctions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuctionParticipants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Everyone who already bid in an auction is a participant (joined at their first bid).
            migrationBuilder.Sql(@"INSERT INTO AuctionParticipants (AuctionId, UserId, JoinedAtUtc)
SELECT AuctionId, UserId, MIN(CreatedAtUtc) FROM Bids GROUP BY AuctionId, UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_UserId_DeviceId",
                table: "DeviceTokens",
                columns: new[] { "UserId", "DeviceId" },
                unique: true,
                filter: "[DeviceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionParticipants_UserId",
                table: "AuctionParticipants",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionParticipants");

            migrationBuilder.DropIndex(
                name: "IX_DeviceTokens_UserId_DeviceId",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "TopicName",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "DeviceTokens");
        }
    }
}
