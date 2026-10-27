using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fidoco.Modules.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PromotionCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DiscountAmount",
                schema: "payments",
                table: "Purchases",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "PromotionCode",
                schema: "payments",
                table: "Purchases",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DiscountAmount",
                schema: "payments",
                table: "PurchaseLines",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "GrossAmount",
                schema: "payments",
                table: "PurchaseLines",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Purchases before promotion codes were paid at list price.
            migrationBuilder.Sql("UPDATE [payments].[PurchaseLines] SET [GrossAmount] = [UnitPrice] * [Quantity];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                schema: "payments",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PromotionCode",
                schema: "payments",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                schema: "payments",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "GrossAmount",
                schema: "payments",
                table: "PurchaseLines");
        }
    }
}
