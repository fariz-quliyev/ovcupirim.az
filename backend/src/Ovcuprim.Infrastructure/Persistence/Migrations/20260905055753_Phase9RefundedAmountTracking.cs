using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ovcuprim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9RefundedAmountTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmountAzn",
                table: "PaymentOrders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_RefundedAmount_NonNegative",
                table: "PaymentOrders",
                sql: "\"RefundedAmountAzn\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_RefundedAmount_NotExceedingAmount",
                table: "PaymentOrders",
                sql: "\"RefundedAmountAzn\" <= \"AmountAzn\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_RefundedAmount_NonNegative",
                table: "PaymentOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_RefundedAmount_NotExceedingAmount",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "RefundedAmountAzn",
                table: "PaymentOrders");
        }
    }
}
