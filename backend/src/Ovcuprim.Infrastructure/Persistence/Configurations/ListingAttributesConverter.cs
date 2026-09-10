using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ovcuprim.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the dynamic attribute bag to a jsonb column.
/// </summary>
/// <remarks>
/// <para>
/// Deserialising into <see cref="JsonElement"/> routes through the framework's element converter,
/// which parses each value into its own document. The elements are therefore independent of any
/// parent document and carry no disposal hazard.
/// </para>
/// <para>
/// <see cref="JsonElement"/> is a struct with no value-based equality, so the comparer below is
/// mandatory: without it EF would either miss mutations or mark the row dirty on every save.
/// Comparison is by raw JSON text per key, which also makes it insensitive to key ordering.
/// </para>
/// </remarks>
internal static class ListingAttributesConverter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ValueConverter<Dictionary<string, JsonElement>, string> Converter { get; } = new(
        value => JsonSerializer.Serialize(value, Options),
        json => Deserialize(json));

    public static ValueComparer<Dictionary<string, JsonElement>> Comparer { get; } = new(
        (left, right) => AreEqual(left, right),
        value => ComputeHash(value),
        value => Snapshot(value));

    private static Dictionary<string, JsonElement> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, Options)
              ?? new Dictionary<string, JsonElement>();

    private static bool AreEqual(Dictionary<string, JsonElement>? left, Dictionary<string, JsonElement>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) ||
                !string.Equals(value.GetRawText(), other.GetRawText(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int ComputeHash(Dictionary<string, JsonElement> value)
    {
        // Order-insensitive: XOR each key/value pair so re-ordered keys hash identically.
        var hash = 0;

        foreach (var (key, element) in value)
        {
            hash ^= HashCode.Combine(key, element.GetRawText());
        }

        return hash;
    }

    private static Dictionary<string, JsonElement> Snapshot(Dictionary<string, JsonElement> value)
    {
        var copy = new Dictionary<string, JsonElement>(value.Count, StringComparer.Ordinal);

        foreach (var (key, element) in value)
        {
            // Clone detaches the element from the document it was parsed from.
            copy[key] = element.Clone();
        }

        return copy;
    }
}
