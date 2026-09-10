using System.Globalization;
using System.Text.Json;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Categories;

public sealed record AttributeValidationResult(
    bool IsValid,
    Dictionary<string, string[]> Errors,
    Dictionary<string, JsonElement> Canonical)
{
    public static AttributeValidationResult Valid(Dictionary<string, JsonElement> canonical) =>
        new(true, [], canonical);
}

public interface IAttributeValidator
{
    /// <summary>
    /// Validates submitted attribute values against a category's effective schema and returns the
    /// canonical JSON to persist. Errors are keyed <c>attributes.{key}</c> so they land on the right
    /// form field through the existing ProblemDetails contract.
    /// </summary>
    AttributeValidationResult Validate(
        IReadOnlyList<AttributeSchemaDto> schema,
        IReadOnlyDictionary<string, JsonElement>? submitted);
}

public sealed class AttributeValidator : IAttributeValidator
{
    public AttributeValidationResult Validate(
        IReadOnlyList<AttributeSchemaDto> schema,
        IReadOnlyDictionary<string, JsonElement>? submitted)
    {
        var values = submitted ?? new Dictionary<string, JsonElement>();
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var canonical = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var known = schema.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        // An unknown key is rejected rather than silently stored, so the bag cannot drift from the schema.
        foreach (var key in values.Keys.Where(k => !known.Contains(k)))
        {
            Add(errors, key, $"Naməlum xüsusiyyət: {key}.");
        }

        foreach (var field in schema)
        {
            var present = values.TryGetValue(field.Key, out var raw) && !IsEmpty(raw);

            if (!present)
            {
                if (field.IsRequired)
                {
                    Add(errors, field.Key, "Bu sahə tələb olunur.");
                }

                continue;
            }

            var dataType = Enum.Parse<AttributeDataType>(field.DataType);

            switch (dataType)
            {
                case AttributeDataType.Text:
                    ValidateText(field, raw, errors, canonical);
                    break;
                case AttributeDataType.Number:
                    ValidateNumber(field, raw, errors, canonical);
                    break;
                case AttributeDataType.Boolean:
                    ValidateBoolean(field, raw, errors, canonical);
                    break;
                case AttributeDataType.Select:
                    ValidateSelect(field, raw, errors, canonical);
                    break;
                case AttributeDataType.MultiSelect:
                    ValidateMultiSelect(field, raw, errors, canonical);
                    break;
                default:
                    Add(errors, field.Key, "Dəstəklənməyən xüsusiyyət tipi.");
                    break;
            }
        }

        return errors.Count == 0
            ? AttributeValidationResult.Valid(canonical)
            : new AttributeValidationResult(
                false,
                errors.ToDictionary(e => $"attributes.{e.Key}", e => e.Value.ToArray(), StringComparer.Ordinal),
                canonical);
    }

    private static void ValidateText(
        AttributeSchemaDto field, JsonElement raw,
        Dictionary<string, List<string>> errors, Dictionary<string, JsonElement> canonical)
    {
        if (raw.ValueKind != JsonValueKind.String)
        {
            Add(errors, field.Key, "Mətn daxil edin.");
            return;
        }

        var text = raw.GetString()?.Trim() ?? string.Empty;

        if (field.MaxLength is { } max && text.Length > max)
        {
            Add(errors, field.Key, $"Maksimum {max} simvol.");
            return;
        }

        canonical[field.Key] = JsonValue(JsonSerializer.Serialize(text));
    }

    private static void ValidateNumber(
        AttributeSchemaDto field, JsonElement raw,
        Dictionary<string, List<string>> errors, Dictionary<string, JsonElement> canonical)
    {
        decimal number;

        switch (raw.ValueKind)
        {
            case JsonValueKind.Number when raw.TryGetDecimal(out var fromNumber):
                number = fromNumber;
                break;

            // A string is accepted on input for form convenience and normalised here: a comma
            // decimal separator is the common Azerbaijani keyboard habit.
            case JsonValueKind.String when TryParseDecimal(raw.GetString(), out var fromText):
                number = fromText;
                break;

            default:
                Add(errors, field.Key, "Yalnız rəqəm daxil edin.");
                return;
        }

        if (field.MinValue is { } min && number < min)
        {
            Add(errors, field.Key, Range(field));
            return;
        }

        if (field.MaxValue is { } max && number > max)
        {
            Add(errors, field.Key, Range(field));
            return;
        }

        var rounded = field.DecimalPlaces is { } places
            ? Math.Round(number, places, MidpointRounding.AwayFromZero)
            : number;

        // Always a JSON number in invariant form, which is what makes the expression index reliable.
        canonical[field.Key] = JsonValue(rounded.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidateBoolean(
        AttributeSchemaDto field, JsonElement raw,
        Dictionary<string, List<string>> errors, Dictionary<string, JsonElement> canonical)
    {
        bool value;

        switch (raw.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                break;
            case JsonValueKind.False:
                value = false;
                break;
            case JsonValueKind.String when bool.TryParse(raw.GetString(), out var parsed):
                value = parsed;
                break;
            default:
                Add(errors, field.Key, "Bəli və ya Xeyr seçin.");
                return;
        }

        canonical[field.Key] = JsonValue(value ? "true" : "false");
    }

    private static void ValidateSelect(
        AttributeSchemaDto field, JsonElement raw,
        Dictionary<string, List<string>> errors, Dictionary<string, JsonElement> canonical)
    {
        if (raw.ValueKind != JsonValueKind.String)
        {
            Add(errors, field.Key, "Siyahıdan bir dəyər seçin.");
            return;
        }

        var value = raw.GetString() ?? string.Empty;

        if (!IsAllowed(field, value))
        {
            Add(errors, field.Key, "Seçilmiş dəyər mövcud deyil.");
            return;
        }

        canonical[field.Key] = JsonValue(JsonSerializer.Serialize(value));
    }

    private static void ValidateMultiSelect(
        AttributeSchemaDto field, JsonElement raw,
        Dictionary<string, List<string>> errors, Dictionary<string, JsonElement> canonical)
    {
        if (raw.ValueKind != JsonValueKind.Array)
        {
            Add(errors, field.Key, "Bir və ya bir neçə dəyər seçin.");
            return;
        }

        var selected = new List<string>();

        foreach (var element in raw.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                Add(errors, field.Key, "Seçilmiş dəyər mövcud deyil.");
                return;
            }

            var value = element.GetString() ?? string.Empty;

            if (!IsAllowed(field, value))
            {
                Add(errors, field.Key, "Seçilmiş dəyər mövcud deyil.");
                return;
            }

            if (!selected.Contains(value, StringComparer.Ordinal))
            {
                selected.Add(value);
            }
        }

        if (selected.Count == 0)
        {
            if (field.IsRequired)
            {
                Add(errors, field.Key, "Bu sahə tələb olunur.");
            }

            return;
        }

        canonical[field.Key] = JsonValue(JsonSerializer.Serialize(selected));
    }

    private static bool IsAllowed(AttributeSchemaDto field, string value) =>
        field.Options is not null && field.Options.Any(o => string.Equals(o.Value, value, StringComparison.Ordinal));

    private static string Range(AttributeSchemaDto field)
    {
        var min = field.MinValue?.ToString(CultureInfo.InvariantCulture) ?? "-∞";
        var max = field.MaxValue?.ToString(CultureInfo.InvariantCulture) ?? "∞";
        var unit = string.IsNullOrEmpty(field.Unit) ? string.Empty : $" {field.Unit}";

        return $"Dəyər {min}–{max}{unit} aralığında olmalıdır.";
    }

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return decimal.TryParse(
            text.Replace(',', '.').Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool IsEmpty(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()),
        JsonValueKind.Array => element.GetArrayLength() == 0,
        _ => false
    };

    private static JsonElement JsonValue(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            errors[key] = list = [];
        }

        list.Add(message);
    }
}
