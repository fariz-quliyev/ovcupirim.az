using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ovcuprim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Replaces PostgreSQL's <c>xmin</c> as the listing concurrency token with an application-managed
    /// <c>Version</c> column, so that the view- and favourite-counter statements — which advance a
    /// tuple's <c>xmin</c> — can no longer make a seller's in-progress edit look stale.
    /// </summary>
    public partial class Phase6ListingVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "xmin" is a PostgreSQL system column. EF scaffolds a DropColumn for it because the
            // model no longer maps it, but it neither can nor should be dropped — the mapping is
            // what went away, not the column. Same hand-edit as Phase4Listings.

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Listings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Listings");

            // Nothing to restore: "xmin" was never created by a migration, only mapped.
        }
    }
}
