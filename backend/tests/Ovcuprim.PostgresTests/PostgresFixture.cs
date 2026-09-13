using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Infrastructure.Persistence;

namespace Ovcuprim.PostgresTests;

/// <summary>Lets a test move time forward without waiting for it.</summary>
public sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } = now;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// A real PostgreSQL database for the handful of things the in-memory provider cannot express.
/// </summary>
/// <remarks>
/// <para>
/// This project exists because of a defect the rest of the suite structurally could not catch:
/// <c>AuditLog.PayloadJson</c> is <c>jsonb</c>, three services wrote plain text into it, and every
/// one of 715 tests passed while listing rejection and all store moderation would have failed
/// against the real database. Provider-specific behaviour needs the provider.
/// </para>
/// <para>
/// <b>No new dependency.</b> Npgsql and EF arrive through the Infrastructure project reference,
/// and the database is the container the repository already uses for development. The connection
/// string comes from <c>OVCUPIRIM_TEST_DB</c>, falling back to the local development container.
/// A database is <b>required</b> for this project — the tests fail with an actionable message
/// without one, because a suite that goes green when the database is missing is precisely the
/// failure mode being fixed. The other two test projects need nothing; CI supplies a service
/// container for this one.
/// </para>
/// <para>
/// Each fixture builds its own throwaway database and drops it afterwards, so a run never touches
/// development data and two runs cannot collide.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DefaultAdminConnection =
        "Host=localhost;Port=5433;Database=postgres;Username=ovcuprim;Password=ovcuprim_dev";

    private readonly string _databaseName = $"ovcupirim_test_{Guid.NewGuid():N}";

    private string? _adminConnectionString;

    /// <summary>Null when no database could be reached; every test then skips.</summary>
    public string? ConnectionString { get; private set; }

    public bool Available => ConnectionString is not null;

    /// <summary>The message a failing test carries when no database could be reached.</summary>
    public string SkipReason { get; private set; } =
        "PostgreSQL is required by this test project. Start the development container "
        + "(docker start ovcuprim-postgres) or set OVCUPIRIM_TEST_DB.";

    public async Task InitializeAsync()
    {
        _adminConnectionString =
            Environment.GetEnvironmentVariable("OVCUPIRIM_TEST_DB") ?? DefaultAdminConnection;

        try
        {
            await using (var admin = new NpgsqlConnection(_adminConnectionString))
            {
                await admin.OpenAsync();

                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            var builder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
            {
                Database = _databaseName
            };

            ConnectionString = builder.ConnectionString;

            // The real migrations, so the schema under test is the schema that ships — including
            // the jsonb columns, the generated tsvector and the partial expression indexes.
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            ConnectionString = null;
            SkipReason = $"PostgreSQL is required by this test project but was not reachable: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (ConnectionString is null || _adminConnectionString is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();

        try
        {
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();

            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
            // A leftover throwaway database is not worth failing a test run over.
        }
    }

    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));

    public AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString ?? DefaultAdminConnection)
            .Options,
        Clock);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
