using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Persistence;
using Ovcuprim.Infrastructure.Persistence.Search;

namespace Ovcuprim.PostgresTests;

/// <summary>
/// The catalogue SQL, executed against PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// This is the class the H-3 finding was about. <c>ListingSearchStore</c> is the only hand-written
/// SQL in the codebase, and until now nothing executed it in CI: the in-memory provider cannot
/// express JSONB containment, <c>safe_numeric</c>, a generated tsvector or a trigram index, so the
/// stand-in re-implements a subset through LINQ and the real statements were verified only by hand.
/// </para>
/// <para>
/// Everything asserted here is a property of the statements themselves — the public predicate, the
/// count cap, the attribute operators the Phase 3 indexes were built for, the folded text fallback,
/// and the rule that a counter refresh must never move a concurrency token.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ListingSearchStoreTests(PostgresFixture fixture)
{
    private void RequireDatabase()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }
    }

    /// <summary>A fresh context and a fresh corpus per test, so counts are exact.</summary>
    private async Task<(AppDbContext Db, ListingSearchStore Store, SearchWorld World)> BuildAsync()
    {
        var db = fixture.CreateContext();

        // Each test gets its own slice of a shared database; clearing keeps the assertions exact.
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE "StoreFollows","Favorites","ListingMedia","ModerationActions","Reports",
                     "Listings","Stores","AttributeOptions","AttributeDefinitions",
                     "Categories","Regions","AuditLogs","RefreshTokens","OtpCodes","Users"
            RESTART IDENTITY CASCADE
            """);

        var world = await SearchWorld.SeedAsync(db, fixture.Clock.UtcNow);

        return (db, new ListingSearchStore(db), world);
    }

    private static ListingQuery Query(params AttributeFilter[] attributes) =>
        new() { Attributes = attributes, PageSize = 24 };

    // ---- the public predicate ------------------------------------------------------------------

    [Fact]
    public async Task Only_live_listings_are_public()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _ = db;

        var page = await store.SearchAsync(Query());

        // Six listings exist; the pending one and the soft-deleted one are not public.
        Assert.Equal(6, await db.Listings.IgnoreQueryFilters().CountAsync());
        Assert.Equal(4, page.Total);
        Assert.Equal(world.ActiveListingIds.Order(), page.Ids.Order());
    }

    [Fact]
    public async Task Results_come_back_newest_bumped_first()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        var page = await store.SearchAsync(Query());

        var bumped = await db.Listings.AsNoTracking()
            .Where(l => page.Ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.BumpedAt);

        var ordered = page.Ids.Select(id => bumped[id]).ToList();

        Assert.Equal(ordered.OrderByDescending(b => b), ordered);
    }

    [Fact]
    public async Task Pagination_walks_the_whole_result_without_repeating_or_dropping()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        var first = await store.SearchAsync(new ListingQuery { Page = 1, PageSize = 3 });
        var second = await store.SearchAsync(new ListingQuery { Page = 2, PageSize = 3 });
        var beyond = await store.SearchAsync(new ListingQuery { Page = 9, PageSize = 3 });

        Assert.Equal(3, first.Ids.Count);
        Assert.Single(second.Ids);
        Assert.Empty(beyond.Ids);

        // Every page reports the same total, and together they cover the set exactly once.
        Assert.Equal(4, first.Total);
        Assert.Equal(4, second.Total);
        Assert.Equal(4, first.Ids.Concat(second.Ids).Distinct().Count());
    }

    [Fact]
    public async Task A_count_below_the_cap_is_exact()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        var page = await store.SearchAsync(Query());

        Assert.True(page.TotalIsExact);
        Assert.Equal(4, page.Total);
    }

    [Fact]
    public async Task The_count_stops_at_the_cap_and_says_so()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        // Counting is the expensive half of a broad query, so it stops and reports "this many or
        // more". Proven by lowering the data to the cap rather than by generating 10 000 rows:
        // the boundary is what matters, and it is asserted from both sides.
        var atCap = await CountWithCapAsync(db, ListingSearchStore.CountCap);

        Assert.Equal(4, atCap);

        var belowCap = await CountWithCapAsync(db, 2);

        // The statement fetches cap + 1 rows to learn whether more exist.
        Assert.Equal(3, belowCap);
    }

    /// <summary>Mirrors the capped-count statement so the boundary can be probed cheaply.</summary>
    private static async Task<int> CountWithCapAsync(AppDbContext db, int cap)
    {
        // Bound, not interpolated — the same discipline the statement it mirrors follows.
        var counts = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value" FROM (
                    SELECT 1 FROM "Listings" WHERE "Status" = 2 AND "DeletedAt" IS NULL LIMIT @p0
                ) t
                """,
                new Npgsql.NpgsqlParameter("p0", cap + 1))
            .ToListAsync();

        return counts[0];
    }

    // ---- dimension filters ----------------------------------------------------------------------

    [Fact]
    public async Task Category_region_price_condition_delivery_and_seller_type_all_narrow()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _db = db;

        Assert.Equal(3, (await store.SearchAsync(new ListingQuery { CategoryIds = [world.TentCategoryId] })).Total);
        Assert.Equal(3, (await store.SearchAsync(new ListingQuery { RegionId = world.BakuRegionId })).Total);
        Assert.Equal(2, (await store.SearchAsync(new ListingQuery { PriceMin = 100 })).Total);
        Assert.Equal(2, (await store.SearchAsync(new ListingQuery { PriceMax = 120 })).Total);
        Assert.Equal(2, (await store.SearchAsync(new ListingQuery { Condition = ListingCondition.New })).Total);
        Assert.Equal(2, (await store.SearchAsync(new ListingQuery { HasDelivery = true })).Total);
        Assert.Equal(1, (await store.SearchAsync(new ListingQuery { SellerType = SellerType.Store })).Total);
        Assert.Equal(1, (await store.SearchAsync(new ListingQuery { StoreId = world.StoreId })).Total);
    }

    [Fact]
    public async Task A_price_filter_never_matches_a_listing_with_no_price()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        // "Razılaşma ilə" is NULL, and NULL is not a number: it is outside every range, not inside.
        var priced = await store.SearchAsync(new ListingQuery { PriceMin = 0 });

        Assert.Equal(3, priced.Total);
    }

    // ---- JSONB attribute filtering ----------------------------------------------------------------

    [Fact]
    public async Task A_select_attribute_matches_by_jsonb_containment()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        var two = await store.SearchAsync(Query(
            new AttributeFilter.Containment("capacity_person", ["""{"capacity_person":"2"}"""])));

        var four = await store.SearchAsync(Query(
            new AttributeFilter.Containment("capacity_person", ["""{"capacity_person":"4"}"""])));

        Assert.Equal(2, two.Total);
        Assert.Equal(1, four.Total);
    }

    [Fact]
    public async Task Several_accepted_values_mean_any_of()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        var either = await store.SearchAsync(Query(
            new AttributeFilter.Containment(
                "capacity_person",
                ["""{"capacity_person":"2"}""", """{"capacity_person":"4"}"""])));

        Assert.Equal(3, either.Total);
    }

    [Fact]
    public async Task A_boolean_attribute_matches_the_json_literal_not_a_string()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        var waterproof = await store.SearchAsync(Query(new AttributeFilter.Flag("waterproof", true)));
        var notWaterproof = await store.SearchAsync(Query(new AttributeFilter.Flag("waterproof", false)));

        Assert.Equal(2, waterproof.Total);
        Assert.Equal(1, notWaterproof.Total);
    }

    [Fact]
    public async Task A_numeric_range_reads_through_safe_numeric()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        // The attribute bag is text in JSON; safe_numeric is what makes a range comparison possible
        // and is the expression the Phase 3 partial indexes are built on.
        Assert.Equal(2, (await store.SearchAsync(Query(new AttributeFilter.Range("weight", 2.0m, 3.5m)))).Total);
        Assert.Equal(1, (await store.SearchAsync(Query(new AttributeFilter.Range("weight", 5m, null)))).Total);
        Assert.Equal(3, (await store.SearchAsync(Query(new AttributeFilter.Range("weight", null, 10m)))).Total);
        Assert.Equal(0, (await store.SearchAsync(Query(new AttributeFilter.Range("weight", 100m, null)))).Total);
    }

    [Fact]
    public async Task Safe_numeric_survives_a_value_that_is_not_a_number()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _db = db;

        // A listing whose weight is nonsense must not take the whole query down with a cast error —
        // which is the entire reason the function exists.
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Listings" SET "Attributes" = jsonb_set("Attributes", '{{weight}}', '"nonsense"')
            WHERE "Id" = @p0
            """,
            new Npgsql.NpgsqlParameter("p0", world.ActiveListingIds[0]));

        var range = await store.SearchAsync(Query(new AttributeFilter.Range("weight", 0m, 100m)));

        Assert.Equal(2, range.Total);
    }

    [Fact]
    public async Task Combining_filters_narrows_rather_than_widens()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _db = db;

        var combined = await store.SearchAsync(new ListingQuery
        {
            CategoryIds = [world.TentCategoryId],
            RegionId = world.BakuRegionId,
            Attributes = [new AttributeFilter.Flag("waterproof", true)],
        });

        Assert.Equal(2, combined.Total);
    }

    // ---- text search --------------------------------------------------------------------------------

    [Fact]
    public async Task Full_text_matches_a_word_from_the_title()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        var found = await store.SearchAsync(new ListingQuery { Text = "tilov", RawText = "tilov" });

        Assert.Equal(1, found.Total);
    }

    [Fact]
    public async Task An_accent_folded_query_still_finds_the_listing()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        // "cadir" must find "Çadır". The tsvector cannot do this — the trigram fallback over the
        // folded SearchKey is what makes Azerbaijani search work, and it only exists in SQL.
        var folded = await store.SearchAsync(new ListingQuery { Text = "cadir", RawText = "cadir" });
        var exact = await store.SearchAsync(new ListingQuery { Text = "cadir", RawText = "çadır" });

        Assert.Equal(3, folded.Total);
        Assert.Equal(3, exact.Total);
    }

    [Fact]
    public async Task Text_that_matches_nothing_returns_nothing_rather_than_everything()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        var none = await store.SearchAsync(new ListingQuery { Text = "avtomobil", RawText = "avtomobil" });

        Assert.Equal(0, none.Total);
    }

    [Fact]
    public async Task Text_composes_with_the_other_filters()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _db = db;

        var narrowed = await store.SearchAsync(new ListingQuery
        {
            Text = "cadir",
            RawText = "cadir",
            RegionId = world.GanjaRegionId,
        });

        Assert.Equal(1, narrowed.Total);
    }

    // ---- facets ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Facets_count_live_listings_per_category_and_region()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _db = db;

        var facets = await store.FacetsAsync(Query());

        Assert.Equal(3, facets.Categories.Single(c => c.Id == world.TentCategoryId).Count);
        Assert.Equal(1, facets.Categories.Single(c => c.Id == world.RodCategoryId).Count);
        Assert.Equal(3, facets.Regions.Single(r => r.Id == world.BakuRegionId).Count);
        Assert.Equal(1, facets.Regions.Single(r => r.Id == world.GanjaRegionId).Count);
    }

    [Fact]
    public async Task A_category_facet_ignores_the_category_filter_but_honours_the_others()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _db = db;

        // Otherwise choosing a category would collapse the list you chose it from to one entry.
        var facets = await store.FacetsAsync(new ListingQuery
        {
            CategoryIds = [world.TentCategoryId],
            RegionId = world.BakuRegionId,
        });

        // The category facet drops its own filter but keeps the region: of the three tents, two are
        // in Bakı. The rod is there too, which is what keeps it selectable in the sidebar.
        Assert.Equal(2, facets.Categories.Single(c => c.Id == world.TentCategoryId).Count);
        Assert.Equal(1, facets.Categories.Single(c => c.Id == world.RodCategoryId).Count);

        // The region facet, conversely, drops the region and keeps the category: tents only.
        Assert.Equal(2, facets.Regions.Single(r => r.Id == world.BakuRegionId).Count);
        Assert.Equal(1, facets.Regions.Single(r => r.Id == world.GanjaRegionId).Count);
    }

    // ---- counters -----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_refresh_recomputes_every_counter_it_owns()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _db = db;

        await store.RefreshListingCountsAsync();

        await using var check = fixture.CreateContext();

        Assert.Equal(3, (await check.Categories.FindAsync(world.TentCategoryId))!.ListingCount);
        Assert.Equal(1, (await check.Categories.FindAsync(world.RodCategoryId))!.ListingCount);
        Assert.Equal(3, (await check.Regions.FindAsync(world.BakuRegionId))!.ListingCount);
        Assert.Equal(1, (await check.Regions.FindAsync(world.GanjaRegionId))!.ListingCount);
        Assert.Equal(1, (await check.Listings.FindAsync(world.FavouritedListingId))!.FavoriteCount);

        var shop = await check.Stores.FindAsync(world.StoreId);

        Assert.Equal(1, shop!.ListingCount);
        Assert.Equal(1, shop.FollowerCount);
    }

    [Fact]
    public async Task Counters_exclude_everything_that_is_not_publicly_live()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _db = db;

        await store.RefreshListingCountsAsync();

        await using var check = fixture.CreateContext();

        // The pending and the soft-deleted tent must not be counted anywhere.
        Assert.Equal(3, (await check.Categories.FindAsync(world.TentCategoryId))!.ListingCount);
        Assert.Equal(4, await check.Listings.CountAsync(l => l.Status == ListingStatus.Active));
    }

    [Fact]
    public async Task A_counter_refresh_never_moves_a_concurrency_token()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _db = db;

        var listingBefore = await db.Listings.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => l.Version);
        var storeBefore = (await db.Stores.AsNoTracking().SingleAsync(s => s.Id == world.StoreId)).Version;

        await store.RefreshListingCountsAsync();
        await store.RefreshListingCountsAsync();

        await using var check = fixture.CreateContext();

        var listingAfter = await check.Listings.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Version);
        var storeAfter = (await check.Stores.AsNoTracking().SingleAsync(s => s.Id == world.StoreId)).Version;

        // This is the M-1 invariant from Phase 6, and the reason those statements never SET Version:
        // a counter flush must not invalidate an edit its owner already has open.
        Assert.Equal(listingBefore, listingAfter);
        Assert.Equal(storeBefore, storeAfter);
    }

    [Fact]
    public async Task The_refresh_is_idempotent_and_reports_only_what_changed()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        var first = await store.RefreshListingCountsAsync();
        var second = await store.RefreshListingCountsAsync();

        Assert.True(first > 0);

        // Nothing moved the second time, so nothing is rewritten.
        Assert.Equal(0, second);
    }

    // ---- view counts ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Buffered_views_are_added_server_side_without_touching_the_token()
    {
        RequireDatabase();

        var (db, store, world) = await BuildAsync();
        await using var _db = db;

        var target = world.ActiveListingIds[0];
        var versionBefore = (await db.Listings.AsNoTracking().SingleAsync(l => l.Id == target)).Version;

        await store.ApplyViewCountsAsync(new Dictionary<Guid, int> { [target] = 5 });
        await store.ApplyViewCountsAsync(new Dictionary<Guid, int> { [target] = 3 });

        await using var check = fixture.CreateContext();
        var listing = await check.Listings.AsNoTracking().SingleAsync(l => l.Id == target);

        // Arithmetic on the server — two flushes accumulate rather than overwrite.
        Assert.Equal(8, listing.ViewCount);
        Assert.Equal(versionBefore, listing.Version);
    }

    [Fact]
    public async Task An_empty_view_flush_does_nothing_at_all()
    {
        RequireDatabase();

        var (db, store, _) = await BuildAsync();
        await using var _db = db;

        Assert.Equal(0, await store.ApplyViewCountsAsync(new Dictionary<Guid, int>()));
    }

    // ---- index eligibility ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_public_predicate_keeps_the_partial_indexes_eligible()
    {
        RequireDatabase();

        var (db, _, _) = await BuildAsync();
        await using var _db = db;

        // The 31 attribute indexes are declared WHERE "Status" = 2, so the predicate has to match
        // for the planner to consider them. This asserts the property that matters and is stable:
        // the literal form is eligible.
        //
        // It deliberately does not assert the converse. A parameterised status is *not* reliably
        // excluded — PostgreSQL 16 can still match a partial index under a custom plan, and which
        // index it picks depends on the data. The literal remains the right choice because it
        // guarantees eligibility regardless of plan type, but "parameterising loses the index" is
        // a stronger claim than the planner actually supports, and a test asserting it would be
        // flaky rather than protective.
        var withLiteral = await ExplainAsync(db,
            """
            SELECT "Id" FROM "Listings"
            WHERE "Status" = 2 AND "DeletedAt" IS NULL
              AND safe_numeric("Attributes" ->> 'weight') >= 1
            """);

        Assert.Contains("IX_Listings_Attr_weight", withLiteral, StringComparison.Ordinal);
    }

    private static async Task<string> ExplainAsync(AppDbContext db, string sql)
    {
        // Small corpus, so the planner would prefer a sequential scan on cost alone; the question
        // here is whether the index is *eligible*, which is what disabling seq scan isolates.
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");

        var statement = sql.Contains("EXPLAIN EXECUTE", StringComparison.Ordinal) ? sql : $"EXPLAIN {sql}";
        var lines = await db.Database.SqlQueryRaw<string>(statement).ToListAsync();

        return string.Join('\n', lines);
    }
}
