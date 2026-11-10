using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERecycleHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BidHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "HeldAmount",
                table: "Bids",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleasedAtUtc",
                table: "Bids",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bid_Held",
                table: "Bids",
                sql: "[HeldAmount] >= 0 AND [HeldAmount] <= [Amount]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Bid_Held",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "HeldAmount",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "ReleasedAtUtc",
                table: "Bids");
        }
    }
}
