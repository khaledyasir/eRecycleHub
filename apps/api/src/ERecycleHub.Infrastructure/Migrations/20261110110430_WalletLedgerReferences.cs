using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERecycleHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WalletLedgerReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BalanceBefore",
                table: "WalletLedger",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReferenceId",
                table: "WalletLedger",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceType",
                table: "WalletLedger",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletLedger_ReferenceType_ReferenceId",
                table: "WalletLedger",
                columns: new[] { "ReferenceType", "ReferenceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WalletLedger_ReferenceType_ReferenceId",
                table: "WalletLedger");

            migrationBuilder.DropColumn(
                name: "BalanceBefore",
                table: "WalletLedger");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "WalletLedger");

            migrationBuilder.DropColumn(
                name: "ReferenceType",
                table: "WalletLedger");
        }
    }
}
