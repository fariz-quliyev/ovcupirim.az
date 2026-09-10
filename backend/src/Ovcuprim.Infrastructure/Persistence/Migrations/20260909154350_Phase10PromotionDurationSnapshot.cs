using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ovcuprim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase10PromotionDurationSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationDays",
                table: "PaymentOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Orders that already exist were priced against their package's duration at the time;
            // the package's current value is the only record of it, so it becomes the snapshot.
            // Anything left at 0 (no package row, which the FK forbids) falls back at read time.
            migrationBuilder.Sql("""
                UPDATE "PaymentOrders" o
                SET "DurationDays" = p."DurationDays"
                FROM "PromotionPackages" p
                WHERE p."Id" = o."PromotionPackageId" AND o."DurationDays" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationDays",
                table: "PaymentOrders");
        }
    }
}
