using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Listings.Search;

namespace Ovcuprim.Infrastructure.Persistence.Search;

/// <summary>
/// The only place in the codebase that writes SQL by hand.
/// </summary>
/// <remarks>
/// <para>
/// The dynamic attribute bag is a JSONB column behind a value converter, so EF cannot translate a
/// predicate over it. These statements reach it directly through the operators the Phase 3 indexes
/// were built for: <c>@&gt;</c> against the GIN index, and <c>safe_numeric("Attributes"-&gt;&gt;'key')</c>
/// against the partial expression indexes.
/// </para>
/// <para>
/// <b>The literal <c>"Status" = 2</c> is load-bearing.</b> Every expression index is declared
/// <c>WHERE "Status" = 2</c>, so a query that parameterises the status, or omits it, silently loses
/// them and falls back to a sequential scan.
/// </para>
/// <para>
/// <b>Injection safety is structural.</b> Attribute keys are whitelisted against the category
/// schema before a query is built, and are the only identifiers ever concatenated. Every value —
/// including every JSON fragment — travels as a bound parameter, so no caller-supplied text reaches
/// the statement text.
/// </para>
/// <para>
/// <b>None of this is covered by an automated test.</b> Both test projects run on the in-memory
/// provider — <c>InMemoryListingSearchStore</c> stands in for this class and re-implements a subset
/// of the filters through LINQ — so the statements below, the facet reader, the counter refresh and
/// the index eligibility they depend on are verified only by hand against a real PostgreSQL
/// instance at the end of each phase. A regression here would ship green. Closing that gap needs a
/// PostgreSQL-backed test project (Testcontainers or a CI service container), which is tracked as
/// pre-launch hardening; until then, changes to this file must be re-verified manually, and the
/// query construction that feeds it is covered provider-independently by
/// <c>ListingQueryParserTests</c>.
/// </para>
/// </remarks>
public sealed class ListingSearchStore(AppDbContext db) : IListingSearchStore
{
    /// <summary>
    /// Counting is the most expensive part of a broad query, so it stops here and reports
    /// "this many or more" instead of scanning the whole match set.
    /// </summary>
    public const int CountCap = 10_000;

    /// <summary>Public listings only. Written as a literal so the partial indexes stay eligible.</summary>
    private const string PublicPredicate = "\"Status\" = 2 AND \"DeletedAt\" IS NULL";

    public async Task<ListingIdPage> SearchAsync(ListingQuery query, CancellationToken cancellationToken = default)
    {
        var where = BuildWhere(query, WhereScope.All);

        var offsetIndex = where.Values.Count;
        var limitIndex = offsetIndex + 1;

        var idSql = $"""
            SELECT "Id" AS "Value" FROM "Listings"
            WHERE {where.Sql}
            ORDER BY {OrderBy(query, where)}
            OFFSET @p{offsetIndex} LIMIT @p{limitIndex}
            """;

        var ids = await db.Database
            .SqlQueryRaw<Guid>(idSql, Parameters([.. where.Values, query.Skip, query.PageSize]))
            .ToListAsync(cancellationToken);

        // Bounded count: the subquery stops at the cap, so a broad filter costs no more than a narrow one.
        var capIndex = where.Values.Count;

        var countSql = $"""
            SELECT count(*)::int AS "Value" FROM (
                SELECT 1 FROM "Listings" WHERE {where.Sql} LIMIT @p{capIndex}
            ) AS capped
            """;

        var total = await db.Database
            .SqlQueryRaw<int>(countSql, Parameters([.. where.Values, CountCap + 1]))
            .SingleAsync(cancellationToken);

        var exact = total <= CountCap;

        return new ListingIdPage(ids, exact ? total : CountCap, exact);
    }

    public async Task<ListingFacets> FacetsAsync(ListingQuery query, CancellationToken cancellationToken = default)
    {
        // Each dimension is counted with its own filter removed, so the list still shows the
        // alternatives a visitor can switch to rather than only the one they already picked.
        var categories = await FacetAsync(query, WhereScope.ExceptCategory, "CategoryId", cancellationToken);
        var regions = await FacetAsync(query, WhereScope.ExceptRegion, "RegionId", cancellationToken);

        return new ListingFacets(categories, regions);
    }

    public async Task<int> RefreshListingCountsAsync(CancellationToken cancellationToken = default)
    {
        // Recomputed wholesale rather than incremented, so a missed increment can never accumulate
        // into a wrong number on the category tree or the region picker. As with the view counter,
        // the FavoriteCount statement below leaves "Version" alone on purpose.
        var categories = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Categories" c
            SET "ListingCount" = COALESCE(t.n, 0)
            FROM (
                SELECT c2."Id" AS cid,
                       (SELECT count(*) FROM "Listings" l
                         WHERE l."CategoryId" = c2."Id" AND l."Status" = 2 AND l."DeletedAt" IS NULL) AS n
                FROM "Categories" c2
            ) t
            WHERE c."Id" = t.cid AND c."ListingCount" IS DISTINCT FROM COALESCE(t.n, 0)
            """,
            cancellationToken);

        var regions = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Regions" r
            SET "ListingCount" = COALESCE(t.n, 0)
            FROM (
                SELECT r2."Id" AS rid,
                       (SELECT count(*) FROM "Listings" l
                         WHERE l."RegionId" = r2."Id" AND l."Status" = 2 AND l."DeletedAt" IS NULL) AS n
                FROM "Regions" r2
            ) t
            WHERE r."Id" = t.rid AND r."ListingCount" IS DISTINCT FROM COALESCE(t.n, 0)
            """,
            cancellationToken);

        var favorites = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Listings" l
            SET "FavoriteCount" = COALESCE(f.n, 0)
            FROM (
                SELECT l2."Id" AS lid,
                       (SELECT count(*) FROM "Favorites" fv WHERE fv."ListingId" = l2."Id") AS n
                FROM "Listings" l2
            ) f
            WHERE l."Id" = f.lid AND l."FavoriteCount" IS DISTINCT FROM COALESCE(f.n, 0)
            """,
            cancellationToken);

        // Storefront counters, on the same terms: recomputed wholesale, and "Version" is left
        // alone so following a store cannot invalidate an edit its owner has open.
        var storeListings = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Stores" st
            SET "ListingCount" = COALESCE(t.n, 0)
            FROM (
                SELECT s2."Id" AS sid,
                       (SELECT count(*) FROM "Listings" l
                         WHERE l."StoreId" = s2."Id" AND l."Status" = 2 AND l."DeletedAt" IS NULL) AS n
                FROM "Stores" s2
            ) t
            WHERE st."Id" = t.sid AND st."ListingCount" IS DISTINCT FROM COALESCE(t.n, 0)
            """,
            cancellationToken);

        var storeFollowers = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Stores" st
            SET "FollowerCount" = COALESCE(t.n, 0)
            FROM (
                SELECT s2."Id" AS sid,
                       (SELECT count(*) FROM "StoreFollows" sf WHERE sf."StoreId" = s2."Id") AS n
                FROM "Stores" s2
            ) t
            WHERE st."Id" = t.sid AND st."FollowerCount" IS DISTINCT FROM COALESCE(t.n, 0)
            """,
            cancellationToken);

        return categories + regions + favorites + storeListings + storeFollowers;
    }

    public async Task<int> ApplyViewCountsAsync(
        IReadOnlyDictionary<Guid, int> increments, CancellationToken cancellationToken = default)
    {
        if (increments.Count == 0)
        {
            return 0;
        }

        // Arithmetic on the server, and deliberately without touching "Version": the listing's
        // concurrency token is application-managed precisely so that counting a view cannot
        // invalidate an edit its owner already has open. Adding a column to this SET would
        // reintroduce that defect.
        var ids = new NpgsqlParameter("p0", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = increments.Keys.ToArray()
        };

        var deltas = new NpgsqlParameter("p1", NpgsqlDbType.Array | NpgsqlDbType.Integer)
        {
            Value = increments.Values.ToArray()
        };

        return await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Listings" l
            SET "ViewCount" = l."ViewCount" + d.delta
            FROM (SELECT unnest(@p0) AS id, unnest(@p1) AS delta) d
            WHERE l."Id" = d.id
            """,
            [ids, deltas],
            cancellationToken);
    }

    private async Task<IReadOnlyList<FacetCount>> FacetAsync(
        ListingQuery query, WhereScope scope, string column, CancellationToken cancellationToken)
    {
        var where = BuildWhere(query, scope);

        var sql = $"""
            SELECT "{column}", count(*)::int
            FROM "Listings"
            WHERE {where.Sql}
            GROUP BY "{column}"
            ORDER BY count(*) DESC
            """;

        // Read directly: two columns per row, which EF's scalar SqlQuery cannot shape.
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;

        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            if (db.Database.CurrentTransaction is { } transaction)
            {
                command.Transaction = transaction.GetDbTransaction();
            }

            foreach (var parameter in Parameters(where.Values))
            {
                command.Parameters.Add(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var results = new List<FacetCount>();

            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new FacetCount(reader.GetInt32(0), reader.GetInt32(1)));
            }

            return results;
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    private enum WhereScope
    {
        All,
        ExceptCategory,
        ExceptRegion
    }

    private sealed record WhereClause(string Sql, List<object> Values, int? TextParameterIndex);

    /// <summary>
    /// The one filter definition. The page, the capped count and both facet counts are all built
    /// from this, so they can never describe different sets.
    /// </summary>
    private static WhereClause BuildWhere(ListingQuery query, WhereScope scope)
    {
        var sql = new StringBuilder(PublicPredicate);
        var values = new List<object>();
        int? textIndex = null;

        if (query.HasText)
        {
            // Both indexes in one pass: exact word matching through the tsvector, and
            // accent-folded fuzzy matching through the trigram index for typos and spelling drift.
            var raw = Add(values, query.RawText ?? query.Text!);

            // Substring match on the folded key, not similarity: pg_trgm's `%` operator scores whole
            // strings against each other, so a short term never clears the threshold against a long
            // title. LIKE with a leading wildcard is what gin_trgm_ops actually accelerates, and it
            // is what makes "cadir" find "Çadır".
            var folded = Add(values, $"%{query.Text!}%");

            textIndex = raw;
            sql.Append($" AND (\"SearchVector\" @@ websearch_to_tsquery('simple', @p{raw}) OR \"SearchKey\" LIKE @p{folded})");
        }

        if (scope != WhereScope.ExceptCategory && query.CategoryIds.Count > 0)
        {
            sql.Append($" AND \"CategoryId\" = ANY(@p{Add(values, query.CategoryIds.ToArray())})");
        }

        if (scope != WhereScope.ExceptRegion && query.RegionId is { } regionId)
        {
            sql.Append($" AND \"RegionId\" = @p{Add(values, regionId)}");
        }

        if (query.StoreId is { } storeId)
        {
            sql.Append($" AND \"StoreId\" = @p{Add(values, storeId)}");
        }

        if (query.PriceMin is { } min)
        {
            sql.Append($" AND \"Price\" >= @p{Add(values, min)}");
        }

        if (query.PriceMax is { } max)
        {
            sql.Append($" AND \"Price\" <= @p{Add(values, max)}");
        }

        if (query.Condition is { } condition)
        {
            sql.Append($" AND \"Condition\" = @p{Add(values, (short)condition)}");
        }

        if (query.HasDelivery is { } delivery)
        {
            sql.Append($" AND \"HasDelivery\" = @p{Add(values, delivery)}");
        }

        if (query.SellerType is { } sellerType)
        {
            sql.Append($" AND \"SellerType\" = @p{Add(values, (short)sellerType)}");
        }

        foreach (var filter in query.Attributes)
        {
            AppendAttribute(sql, values, filter);
        }

        return new WhereClause(sql.ToString(), values, textIndex);
    }

    private static void AppendAttribute(StringBuilder sql, List<object> values, AttributeFilter filter)
    {
        // The key is the only caller-influenced text that reaches the statement, and it has already
        // been matched against the category schema — an unknown key never gets this far.
        var key = filter.Key;

        switch (filter)
        {
            case AttributeFilter.Range range:
                if (range.Min is { } low)
                {
                    sql.Append($" AND safe_numeric(\"Attributes\" ->> '{key}') >= @p{Add(values, low)}");
                }

                if (range.Max is { } high)
                {
                    sql.Append($" AND safe_numeric(\"Attributes\" ->> '{key}') <= @p{Add(values, high)}");
                }

                break;

            case AttributeFilter.Containment containment when containment.JsonValues.Count > 0:
                // Several accepted values mean "any of", so they are OR-ed inside one group.
                sql.Append(" AND (");

                for (var i = 0; i < containment.JsonValues.Count; i++)
                {
                    if (i > 0)
                    {
                        sql.Append(" OR ");
                    }

                    sql.Append($"\"Attributes\" @> CAST(@p{Add(values, containment.JsonValues[i])} AS jsonb)");
                }

                sql.Append(')');
                break;

            case AttributeFilter.Flag flag:
                var json = $$"""{"{{key}}": {{(flag.Value ? "true" : "false")}}}""";
                sql.Append($" AND \"Attributes\" @> CAST(@p{Add(values, json)} AS jsonb)");
                break;
        }
    }

    private static int Add(List<object> values, object value)
    {
        values.Add(value);
        return values.Count - 1;
    }

    /// <summary>Fresh parameter instances per command — a DbParameter cannot be shared between two.</summary>
    private static DbParameter[] Parameters(IReadOnlyList<object> values) =>
        [.. values.Select((value, index) => new NpgsqlParameter($"p{index}", value))];

    private static string OrderBy(ListingQuery query, WhereClause where) => query.Sort switch
    {
        // A missing price means "Razılaşma ilə"; those sort last either way rather than dominating
        // one end of the range.
        ListingSort.PriceAscending => "\"Price\" ASC NULLS LAST, \"Id\" DESC",
        ListingSort.PriceDescending => "\"Price\" DESC NULLS LAST, \"Id\" DESC",
        ListingSort.Relevance when where.TextParameterIndex is { } index =>
            $"ts_rank(\"SearchVector\", websearch_to_tsquery('simple', @p{index})) DESC, \"BumpedAt\" DESC NULLS LAST, \"Id\" DESC",
        // The id tiebreak keeps paging stable when many rows share a timestamp.
        _ => "\"BumpedAt\" DESC NULLS LAST, \"Id\" DESC"
    };
}
