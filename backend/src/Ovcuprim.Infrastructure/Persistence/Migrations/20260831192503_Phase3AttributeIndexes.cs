using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ovcuprim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3AttributeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A cast that cannot throw. Index expressions must be IMMUTABLE, and a single
            // malformed value must degrade to NULL rather than break the index or the write path.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION safe_numeric(value text)
                RETURNS numeric
                LANGUAGE plpgsql
                IMMUTABLE
                STRICT
                PARALLEL SAFE
                AS $$
                BEGIN
                    RETURN value::numeric;
                EXCEPTION WHEN others THEN
                    RETURN NULL;
                END;
                $$;");

            // One expression index per distinct numeric filterable key (31). Phase 5 must use the
            // identical expression and the same Status filter, or the planner will not use these.
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_battery_life\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'battery_life')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_bearings\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'bearings')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_blade_length\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'blade_length')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_breaking_strength\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'breaking_strength')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_breathability\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'breathability')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_capacity\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'capacity')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_comfort_temp\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'comfort_temp')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_detection_range\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'detection_range')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_diameter\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'diameter')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_engine_power\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'engine_power')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_grit\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'grit')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_heat_retention\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'heat_retention')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_length\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'length')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_lumens\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'lumens')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_lure_length\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'lure_length')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_lure_weight\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'lure_weight')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_max_load\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'max_load')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_objective_diameter\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'objective_diameter')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_power\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'power')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_r_value\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'r_value')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_screen_size\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'screen_size')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_sections\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'sections')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_temp_rating\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'temp_rating')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_test_max\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'test_max')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_test_min\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'test_min')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_thickness\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'thickness')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_total_length\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'total_length')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_volume\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'volume')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_waterproof_mm\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'waterproof_mm')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_weight\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'weight')) WHERE \"Status\" = 2;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Listings_Attr_width\" ON \"Listings\" (safe_numeric(\"Attributes\" ->> 'width')) WHERE \"Status\" = 2;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_battery_life\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_bearings\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_blade_length\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_breaking_strength\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_breathability\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_capacity\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_comfort_temp\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_detection_range\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_diameter\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_engine_power\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_grit\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_heat_retention\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_length\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_lumens\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_lure_length\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_lure_weight\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_max_load\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_objective_diameter\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_power\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_r_value\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_screen_size\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_sections\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_temp_rating\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_test_max\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_test_min\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_thickness\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_total_length\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_volume\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_waterproof_mm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_weight\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Listings_Attr_width\";");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS safe_numeric(text);");
        }
    }
}
