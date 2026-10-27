using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fidoco.Modules.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Settlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "NetAmount",
                schema: "payments",
                table: "PurchaseLines",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "PlatformFeeAmount",
                schema: "payments",
                table: "PurchaseLines",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CommissionFixedAmountPerTicket",
                schema: "payments",
                table: "CheckoutSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionPercent",
                schema: "payments",
                table: "CheckoutSessions",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NetAmount",
                schema: "payments",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "PlatformFeeAmount",
                schema: "payments",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "CommissionFixedAmountPerTicket",
                schema: "payments",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "CommissionPercent",
                schema: "payments",
                table: "CheckoutSessions");
        }
    }
}
