using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fidoco.Modules.Playback.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GrantedBy",
                schema: "playback",
                table: "Entitlements",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                schema: "playback",
                table: "Entitlements",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_TenantId_EventId_SourceType",
                schema: "playback",
                table: "Entitlements",
                columns: new[] { "TenantId", "EventId", "SourceType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Entitlements_TenantId_EventId_SourceType",
                schema: "playback",
                table: "Entitlements");

            migrationBuilder.DropColumn(
                name: "GrantedBy",
                schema: "playback",
                table: "Entitlements");

            migrationBuilder.DropColumn(
                name: "Note",
                schema: "playback",
                table: "Entitlements");
        }
    }
}
