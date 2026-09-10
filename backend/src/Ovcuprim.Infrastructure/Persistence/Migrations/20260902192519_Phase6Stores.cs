using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ovcuprim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Activates the storefront schema that has been present and unused since Phase 1: image keys,
    /// denormalised counters and the concurrency token.
    /// </summary>
    public partial class Phase6Stores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LogoMediaId was an unconstrained Guid pointing at nothing: ListingMedia is
            // listing-scoped and cannot hold a storefront image. Replaced below by plain storage
            // keys. The column has never held a value.
            migrationBuilder.DropColumn(
                name: "LogoMediaId",
                table: "Stores");

            migrationBuilder.AddColumn<string>(
                name: "BannerStorageKey",
                table: "Stores",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FollowerCount",
                table: "Stores",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ListingCount",
                table: "Stores",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LogoStorageKey",
                table: "Stores",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Stores",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Stores_Status_Name",
                table: "Stores",
                columns: new[] { "Status", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stores_Status_Name",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "BannerStorageKey",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "FollowerCount",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "ListingCount",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "LogoStorageKey",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Stores");

            migrationBuilder.AddColumn<Guid>(
                name: "LogoMediaId",
                table: "Stores",
                type: "uuid",
                nullable: true);
        }
    }
}
