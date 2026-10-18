using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERecycleHub.Infrastructure.Migrations
{
    /// <summary>
    /// One account type for customers and sellers. Hand-edited from the scaffold:
    /// <list type="bullet">
    /// <item>Users with the legacy Publisher role (2) become customers (3); their Publisher row is what makes them sellers.</item>
    /// <item>Applications now belong to a signed-in user. Approved ones are linked to the account they created; anonymous pending/rejected ones
    /// (which only held an e-mail and a password hash for a person with no account) are deleted, and every stored applicant password hash is dropped.</item>
    /// <item>PasswordHash is dropped, NOT renamed: a new RejectionReason column is added instead, so no hash can ever surface as a reason.</item>
    /// </list>
    /// </summary>
    public partial class SellerApplications : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "UserId", table: "PublisherApplications", type: "int", nullable: true);
            migrationBuilder.AddColumn<string>(name: "RejectionReason", table: "PublisherApplications", type: "nvarchar(1000)", maxLength: 1000, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Address", table: "PublisherApplications", type: "nvarchar(1000)", maxLength: 1000, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "CrDocumentFileId", table: "PublisherApplications", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "IdDocumentFileId", table: "PublisherApplications", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "VatCertificateFileId", table: "PublisherApplications", type: "uniqueidentifier", nullable: true);

            // Data: link approved applications to their account, drop the ones with no account, fit values to the narrower columns.
            migrationBuilder.Sql("UPDATE [PublisherApplications] SET [UserId] = [CreatedUserId] WHERE [CreatedUserId] IS NOT NULL");
            migrationBuilder.Sql("DELETE FROM [PublisherApplications] WHERE [UserId] IS NULL OR NOT EXISTS (SELECT 1 FROM [Users] u WHERE u.[Id] = [PublisherApplications].[UserId])");
            migrationBuilder.Sql("UPDATE [PublisherApplications] SET [Name] = LEFT([Name], 200), [Company] = LEFT([Company], 200), [Mobile] = LEFT([Mobile], 50)");
            migrationBuilder.Sql("UPDATE a SET a.[CountryId] = u.[CountryId] FROM [PublisherApplications] a JOIN [Users] u ON u.[Id] = a.[UserId] WHERE a.[CountryId] IS NULL");
            migrationBuilder.Sql("UPDATE [Users] SET [Role] = 3 WHERE [Role] = 2");

            migrationBuilder.DropColumn(name: "CreatedUserId", table: "PublisherApplications");
            migrationBuilder.DropColumn(name: "Email", table: "PublisherApplications");
            migrationBuilder.DropColumn(name: "PasswordHash", table: "PublisherApplications");

            migrationBuilder.AlterColumn<int>(name: "UserId", table: "PublisherApplications", type: "int", nullable: false, oldClrType: typeof(int), oldType: "int", oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "Name", table: "PublisherApplications", type: "nvarchar(200)", maxLength: 200, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(400)", oldMaxLength: 400);
            migrationBuilder.AlterColumn<string>(name: "Mobile", table: "PublisherApplications", type: "nvarchar(50)", maxLength: 50, nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(400)", oldMaxLength: 400, oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "Company", table: "PublisherApplications", type: "nvarchar(200)", maxLength: 200, nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(400)", oldMaxLength: 400, oldNullable: true);
            migrationBuilder.AlterColumn<int>(name: "CountryId", table: "PublisherApplications", type: "int", nullable: false, oldClrType: typeof(int), oldType: "int", oldNullable: true);

            migrationBuilder.CreateIndex(name: "IX_PublisherApplications_CrDocumentFileId", table: "PublisherApplications", column: "CrDocumentFileId");
            migrationBuilder.CreateIndex(name: "IX_PublisherApplications_IdDocumentFileId", table: "PublisherApplications", column: "IdDocumentFileId");
            migrationBuilder.CreateIndex(name: "IX_PublisherApplications_VatCertificateFileId", table: "PublisherApplications", column: "VatCertificateFileId");
            migrationBuilder.CreateIndex(name: "IX_PublisherApplications_UserId", table: "PublisherApplications", column: "UserId", unique: true, filter: "[Status] = 0");

            migrationBuilder.AddForeignKey(name: "FK_PublisherApplications_Files_CrDocumentFileId", table: "PublisherApplications", column: "CrDocumentFileId",
                principalTable: "Files", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_PublisherApplications_Files_IdDocumentFileId", table: "PublisherApplications", column: "IdDocumentFileId",
                principalTable: "Files", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_PublisherApplications_Files_VatCertificateFileId", table: "PublisherApplications", column: "VatCertificateFileId",
                principalTable: "Files", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_PublisherApplications_Users_UserId", table: "PublisherApplications", column: "UserId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_PublisherApplications_Files_CrDocumentFileId", table: "PublisherApplications");
            migrationBuilder.DropForeignKey(name: "FK_PublisherApplications_Files_IdDocumentFileId", table: "PublisherApplications");
            migrationBuilder.DropForeignKey(name: "FK_PublisherApplications_Files_VatCertificateFileId", table: "PublisherApplications");
            migrationBuilder.DropForeignKey(name: "FK_PublisherApplications_Users_UserId", table: "PublisherApplications");
            migrationBuilder.DropIndex(name: "IX_PublisherApplications_CrDocumentFileId", table: "PublisherApplications");
            migrationBuilder.DropIndex(name: "IX_PublisherApplications_IdDocumentFileId", table: "PublisherApplications");
            migrationBuilder.DropIndex(name: "IX_PublisherApplications_UserId", table: "PublisherApplications");
            migrationBuilder.DropIndex(name: "IX_PublisherApplications_VatCertificateFileId", table: "PublisherApplications");

            migrationBuilder.AddColumn<string>(name: "Email", table: "PublisherApplications", type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "PasswordHash", table: "PublisherApplications", type: "nvarchar(1000)", maxLength: 1000, nullable: true);
            migrationBuilder.AddColumn<int>(name: "CreatedUserId", table: "PublisherApplications", type: "int", nullable: true);
            // Best effort: restore the e-mail and created-user link from the account, and the legacy Publisher role for accounts that own a store.
            migrationBuilder.Sql("UPDATE a SET a.[Email] = u.[Email], a.[CreatedUserId] = CASE WHEN a.[Status] = 1 THEN a.[UserId] END FROM [PublisherApplications] a JOIN [Users] u ON u.[Id] = a.[UserId]");
            migrationBuilder.Sql("UPDATE [Users] SET [Role] = 2 WHERE [Role] = 3 AND EXISTS (SELECT 1 FROM [Publishers] p WHERE p.[UserId] = [Users].[Id])");

            migrationBuilder.DropColumn(name: "Address", table: "PublisherApplications");
            migrationBuilder.DropColumn(name: "CrDocumentFileId", table: "PublisherApplications");
            migrationBuilder.DropColumn(name: "IdDocumentFileId", table: "PublisherApplications");
            migrationBuilder.DropColumn(name: "VatCertificateFileId", table: "PublisherApplications");
            migrationBuilder.DropColumn(name: "RejectionReason", table: "PublisherApplications");
            migrationBuilder.DropColumn(name: "UserId", table: "PublisherApplications");

            migrationBuilder.AlterColumn<int>(name: "CountryId", table: "PublisherApplications", type: "int", nullable: true, oldClrType: typeof(int), oldType: "int");
            migrationBuilder.AlterColumn<string>(name: "Name", table: "PublisherApplications", type: "nvarchar(400)", maxLength: 400, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(200)", oldMaxLength: 200);
            migrationBuilder.AlterColumn<string>(name: "Mobile", table: "PublisherApplications", type: "nvarchar(400)", maxLength: 400, nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(50)", oldMaxLength: 50, oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "Company", table: "PublisherApplications", type: "nvarchar(400)", maxLength: 400, nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(200)", oldMaxLength: 200, oldNullable: true);
        }
    }
}
