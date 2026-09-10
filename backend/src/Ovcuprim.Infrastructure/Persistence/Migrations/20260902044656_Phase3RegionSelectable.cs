using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ovcuprim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3RegionSelectable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows are practical places, so they backfill as selectable; internal
            // administrative records are marked false explicitly by the authoritative import.
            migrationBuilder.AddColumn<bool>(
                name: "IsSelectable",
                table: "Regions",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSelectable",
                table: "Regions");
        }
    }
}
