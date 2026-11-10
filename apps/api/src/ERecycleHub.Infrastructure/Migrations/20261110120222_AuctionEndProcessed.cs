using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERecycleHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuctionEndProcessed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndProcessedAtUtc",
                table: "Auctions",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndProcessedAtUtc",
                table: "Auctions");
        }
    }
}
