using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERecycleHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HomeHero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HomeHeroes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BadgeEn = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    BadgeAr = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TitleEn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TitleAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LeadEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LeadAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PrimaryCtaEn = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PrimaryCtaAr = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PrimaryCtaUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SecondaryCtaEn = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SecondaryCtaAr = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SecondaryCtaUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CardTitleEn = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CardTitleAr = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ImageFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ImageAltEn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ImageAltAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ShowSearch = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeHeroes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HomeHeroes_Files_ImageFileId",
                        column: x => x.ImageFileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HomeHeroes_ImageFileId",
                table: "HomeHeroes",
                column: "ImageFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HomeHeroes");
        }
    }
}
