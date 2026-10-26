using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fidoco.Modules.Playback.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "playback");

            migrationBuilder.CreateTable(
                name: "Entitlements",
                schema: "playback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncludesLive = table.Column<bool>(type: "bit", nullable: false),
                    ReplayAccessDays = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entitlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaybackOutbox",
                schema: "playback",
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
                    table.PrimaryKey("PK_PlaybackOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_SourceType_SourceId",
                schema: "playback",
                table: "Entitlements",
                columns: new[] { "SourceType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_TenantId_CustomerId_EventId",
                schema: "playback",
                table: "Entitlements",
                columns: new[] { "TenantId", "CustomerId", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackOutbox_ProcessedAt_FailedAt_NextAttemptAt",
                schema: "playback",
                table: "PlaybackOutbox",
                columns: new[] { "ProcessedAt", "FailedAt", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Entitlements",
                schema: "playback");

            migrationBuilder.DropTable(
                name: "PlaybackOutbox",
                schema: "playback");
        }
    }
}
