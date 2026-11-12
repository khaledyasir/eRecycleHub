using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERecycleHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SessionSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "UserSessions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // Sessions issued before this column keep working until the next password change: copy each user's current stamp.
            migrationBuilder.Sql("UPDATE s SET s.SecurityStamp = u.SecurityStamp FROM UserSessions s JOIN Users u ON u.Id = s.UserId;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "UserSessions");
        }
    }
}
