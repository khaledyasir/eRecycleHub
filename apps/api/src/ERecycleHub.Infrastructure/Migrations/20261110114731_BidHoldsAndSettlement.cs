using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERecycleHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BidHoldsAndSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventKey",
                table: "Notifications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PushedAtUtc",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "DeviceTokens",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettledAtUtc",
                table: "Bids",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettlementFailedAtUtc",
                table: "Auctions",
                type: "datetime2",
                nullable: true);

            // Bids held under the old debit-at-bid model (never turned on in production, so normally a no-op) become holds again.
            // Dynamic SQL: the script reads Bids.SettledAtUtc, which this same migration adds. In a generated (idempotent) script both would sit in one
            // batch, and SQL Server checks column names for the whole batch before the ALTER TABLE has run (error 207). EXEC defers the check.
            migrationBuilder.Sql("EXEC(N'" + ERecycleHub.Infrastructure.HoldModelConversion.Script.Replace("'", "''") + "');");

            migrationBuilder.CreateIndex(
                name: "UX_WalletLedger_AuctionPayment",
                table: "WalletLedger",
                columns: new[] { "ReferenceType", "ReferenceId", "Reason" },
                unique: true,
                filter: "[Reason] = 9");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_EventKey",
                table: "Notifications",
                columns: new[] { "UserId", "EventKey" },
                unique: true,
                filter: "[EventKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bids_UserId_ReleasedAtUtc_SettledAtUtc",
                table: "Bids",
                columns: new[] { "UserId", "ReleasedAtUtc", "SettledAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_WalletLedger_AuctionPayment",
                table: "WalletLedger");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_EventKey",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Bids_UserId_ReleasedAtUtc_SettledAtUtc",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "EventKey",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "PushedAtUtc",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "SettledAtUtc",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "SettlementFailedAtUtc",
                table: "Auctions");
        }
    }
}
