using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fidoco.Modules.Tenants.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CommercialTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CommissionFixedAmountPerTicket",
                schema: "tenants",
                table: "Tenants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionPercent",
                schema: "tenants",
                table: "Tenants",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "OrganizerVatRegistered",
                schema: "tenants",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionFixedAmountPerTicket",
                schema: "tenants",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CommissionPercent",
                schema: "tenants",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "OrganizerVatRegistered",
                schema: "tenants",
                table: "Tenants");
        }
    }
}
