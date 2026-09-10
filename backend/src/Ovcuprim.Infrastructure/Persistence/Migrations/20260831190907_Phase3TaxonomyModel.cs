using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ovcuprim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3TaxonomyModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Regions_ParentId",
                table: "Regions");

            // IsRestricted is superseded by the tri-state RestrictionStatus. It is dropped rather
            // than renamed: EF inferred a rename to IsSelectable, but the two flags carry opposite
            // semantics (false = not restricted vs false = not selectable), so carrying the values
            // across would flip the meaning on any populated database.
            migrationBuilder.DropColumn(
                name: "IsRestricted",
                table: "Categories");

            migrationBuilder.AddColumn<bool>(
                name: "IsSelectable",
                table: "Categories",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverImageKey",
                table: "StaticPages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcerptAz",
                table: "StaticPages",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcerptRu",
                table: "StaticPages",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaDescriptionAz",
                table: "StaticPages",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaDescriptionRu",
                table: "StaticPages",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "PageType",
                table: "StaticPages",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "StaticPages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Depth",
                table: "Regions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Regions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ListingCount",
                table: "Regions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<short>(
                name: "Type",
                table: "Regions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgeConfirmedAt",
                table: "Listings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Depth",
                table: "Categories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAz",
                table: "Categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionRu",
                table: "Categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageKey",
                table: "Categories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaDescriptionAz",
                table: "Categories",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaDescriptionRu",
                table: "Categories",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaTitleAz",
                table: "Categories",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaTitleRu",
                table: "Categories",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousSlug",
                table: "Categories",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            // Default is Unclassified (1): a category is never silently treated as cleared.
            migrationBuilder.AddColumn<short>(
                name: "RestrictionStatus",
                table: "Categories",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AttributeOptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AppliesToDescendants",
                table: "AttributeDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DecimalPlaces",
                table: "AttributeDefinitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HelpTextAz",
                table: "AttributeDefinitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HelpTextRu",
                table: "AttributeDefinitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AttributeDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSearchable",
                table: "AttributeDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxLength",
                table: "AttributeDefinitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxValue",
                table: "AttributeDefinitions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinValue",
                table: "AttributeDefinitions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceholderAz",
                table: "AttributeDefinitions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceholderRu",
                table: "AttributeDefinitions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaticPages_PageType_IsPublished_SortOrder",
                table: "StaticPages",
                columns: new[] { "PageType", "IsPublished", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Regions_IsActive",
                table: "Regions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_ParentId_SortOrder",
                table: "Regions",
                columns: new[] { "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_IsActive",
                table: "Categories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId_SortOrder",
                table: "Categories",
                columns: new[] { "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_RootSlug",
                table: "Categories",
                column: "Slug",
                unique: true,
                filter: "\"ParentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeOptions_AttributeDefinitionId_SortOrder",
                table: "AttributeOptions",
                columns: new[] { "AttributeDefinitionId", "SortOrder" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttributeOptions_Value_Slug",
                table: "AttributeOptions",
                sql: "\"Value\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeDefinitions_CategoryId_SortOrder",
                table: "AttributeDefinitions",
                columns: new[] { "CategoryId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaticPages_PageType_IsPublished_SortOrder",
                table: "StaticPages");

            migrationBuilder.DropIndex(
                name: "IX_Regions_IsActive",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Regions_ParentId_SortOrder",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Categories_IsActive",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_ParentId_SortOrder",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_RootSlug",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_AttributeOptions_AttributeDefinitionId_SortOrder",
                table: "AttributeOptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AttributeOptions_Value_Slug",
                table: "AttributeOptions");

            migrationBuilder.DropIndex(
                name: "IX_AttributeDefinitions_CategoryId_SortOrder",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "CoverImageKey",
                table: "StaticPages");

            migrationBuilder.DropColumn(
                name: "ExcerptAz",
                table: "StaticPages");

            migrationBuilder.DropColumn(
                name: "ExcerptRu",
                table: "StaticPages");

            migrationBuilder.DropColumn(
                name: "MetaDescriptionAz",
                table: "StaticPages");

            migrationBuilder.DropColumn(
                name: "MetaDescriptionRu",
                table: "StaticPages");

            migrationBuilder.DropColumn(
                name: "PageType",
                table: "StaticPages");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "StaticPages");

            migrationBuilder.DropColumn(
                name: "Depth",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "ListingCount",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "AgeConfirmedAt",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "Depth",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "DescriptionAz",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "DescriptionRu",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "ImageKey",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "MetaDescriptionAz",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "MetaDescriptionRu",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "MetaTitleAz",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "MetaTitleRu",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "PreviousSlug",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "RestrictionStatus",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AttributeOptions");

            migrationBuilder.DropColumn(
                name: "AppliesToDescendants",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "DecimalPlaces",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "HelpTextAz",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "HelpTextRu",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "IsSearchable",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "MaxLength",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "MaxValue",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "MinValue",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "PlaceholderAz",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "PlaceholderRu",
                table: "AttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "IsSelectable",
                table: "Categories");

            migrationBuilder.AddColumn<bool>(
                name: "IsRestricted",
                table: "Categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_ParentId",
                table: "Regions",
                column: "ParentId");
        }
    }
}
