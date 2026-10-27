using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fidoco.Modules.Support.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "support");

            migrationBuilder.CreateTable(
                name: "SupportLog",
                schema: "support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupportOutbox",
                schema: "support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    HandlerType = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LockedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportLog_TenantId_CreatedAt",
                schema: "support",
                table: "SupportLog",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportLog_TenantId_CustomerId",
                schema: "support",
                table: "SupportLog",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportOutbox_ProcessedAt_FailedAt_NextAttemptAt",
                schema: "support",
                table: "SupportOutbox",
                columns: new[] { "ProcessedAt", "FailedAt", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportLog",
                schema: "support");

            migrationBuilder.DropTable(
                name: "SupportOutbox",
                schema: "support");
        }
    }
}
