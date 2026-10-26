using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fidoco.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "CatalogOutbox",
                schema: "catalog",
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
                    table.PrimaryKey("PK_CatalogOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogProcessedWebhooks",
                schema: "catalog",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProcessedWebhooks", x => new { x.Provider, x.EventId });
                });

            migrationBuilder.CreateTable(
                name: "Events",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LiveStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Streams",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderStreamId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LivePlaybackId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StatusChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LatencyMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReconnectWindowSeconds = table.Column<int>(type: "int", nullable: false),
                    PrimaryRecordingChosenByAdmin = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Streams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Streams_Events_EventId",
                        column: x => x.EventId,
                        principalSchema: "catalog",
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketTypes",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AccessMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PriceAmount = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    IncludesLive = table.Column<bool>(type: "bit", nullable: false),
                    ReplayAccessDays = table.Column<int>(type: "int", nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    SalesStartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SalesEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketTypes_Events_EventId",
                        column: x => x.EventId,
                        principalSchema: "catalog",
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StreamRecordings",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StreamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderAssetId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PlaybackId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DurationSeconds = table.Column<double>(type: "float", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StreamRecordings_Streams_StreamId",
                        column: x => x.StreamId,
                        principalSchema: "catalog",
                        principalTable: "Streams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogOutbox_ProcessedAt_FailedAt_NextAttemptAt",
                schema: "catalog",
                table: "CatalogOutbox",
                columns: new[] { "ProcessedAt", "FailedAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_TenantId_Slug",
                schema: "catalog",
                table: "Events",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_TenantId_StartsAt",
                schema: "catalog",
                table: "Events",
                columns: new[] { "TenantId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StreamRecordings_ProviderAssetId",
                schema: "catalog",
                table: "StreamRecordings",
                column: "ProviderAssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StreamRecordings_StreamId",
                schema: "catalog",
                table: "StreamRecordings",
                column: "StreamId");

            migrationBuilder.CreateIndex(
                name: "IX_Streams_EventId",
                schema: "catalog",
                table: "Streams",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Streams_ProviderStreamId",
                schema: "catalog",
                table: "Streams",
                column: "ProviderStreamId",
                unique: true,
                filter: "[ProviderStreamId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TicketTypes_EventId",
                schema: "catalog",
                table: "TicketTypes",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogOutbox",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "CatalogProcessedWebhooks",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "StreamRecordings",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TicketTypes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Streams",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Events",
                schema: "catalog");
        }
    }
}
