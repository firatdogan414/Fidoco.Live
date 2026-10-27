using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fidoco.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdminSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                schema: "identity",
                table: "PlatformAdmins",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                schema: "identity",
                table: "AdminUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_AdminTokens_AdminKind_AdminId",
                schema: "identity",
                table: "AdminTokens",
                columns: new[] { "AdminKind", "AdminId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminTokens_AdminKind_AdminId",
                schema: "identity",
                table: "AdminTokens");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                schema: "identity",
                table: "PlatformAdmins");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                schema: "identity",
                table: "AdminUsers");
        }
    }
}
