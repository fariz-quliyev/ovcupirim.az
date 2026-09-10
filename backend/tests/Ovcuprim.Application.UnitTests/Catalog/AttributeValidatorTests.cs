using System.Text.Json;
using Ovcuprim.Application.Categories;

namespace Ovcuprim.Application.UnitTests.Catalog;

public class AttributeValidatorTests
{
    private static async Task<(TaxonomyTestHarness Harness, IReadOnlyList<AttributeSchemaDto> Schema)> ChildSchemaAsync()
    {
        var harness = new TaxonomyTestHarness();
        await harness.BuildFixtureAsync();
        var schema = (await harness.Categories.GetSchemaAsync("child")).Value!.Attributes;
        return (harness, schema);
    }

    private static JsonElement J(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static Dictionary<string, JsonElement> Values(params (string Key, string Json)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => J(p.Json), StringComparer.Ordinal);

    [Fact]
    public async Task Accepts_a_complete_valid_submission()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(
            ("size", "\"41\""),
            ("length", "2.4"),
            ("features", "[\"a\",\"b\"]"),
            ("waterproof", "true"),
            ("brand_like", "\"Shimano\"")));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Reports_a_missing_required_attribute()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(("size", "\"41\"")));

        Assert.False(result.IsValid);
        Assert.Contains("attributes.length", result.Errors.Keys);
    }

    [Fact]
    public async Task Rejects_an_unknown_key_rather_than_storing_it()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(
            ("size", "\"41\""), ("length", "2.4"), ("nonsense", "\"x\"")));

        Assert.False(result.IsValid);
        Assert.Contains("attributes.nonsense", result.Errors.Keys);
        Assert.DoesNotContain("nonsense", result.Canonical.Keys);
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    public async Task Rejects_a_non_numeric_value_for_a_number(string json)
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(("size", "\"41\""), ("length", json)));

        Assert.False(result.IsValid);
        Assert.Contains("attributes.length", result.Errors.Keys);
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("7")]
    public async Task Rejects_a_number_outside_its_bounds(string json)
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(("size", "\"41\""), ("length", json)));

        Assert.False(result.IsValid);
        Assert.Contains("attributes.length", result.Errors.Keys);
    }

    [Fact]
    public async Task Canonicalises_a_comma_decimal_and_rounds_to_the_configured_places()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(("size", "\"41\""), ("length", "\"2,406\"")));

        Assert.True(result.IsValid);

        var length = result.Canonical["length"];
        Assert.Equal(JsonValueKind.Number, length.ValueKind);
        Assert.Equal(2.41m, length.GetDecimal());
    }

    [Fact]
    public async Task Stores_numbers_as_json_numbers_not_strings()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(("size", "\"41\""), ("length", "\"3\"")));

        // The expression index depends on the value being a JSON number.
        Assert.Equal(JsonValueKind.Number, result.Canonical["length"].ValueKind);
    }

    [Fact]
    public async Task Rejects_a_select_value_outside_the_active_option_set()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(("size", "\"99\""), ("length", "2.4")));

        Assert.False(result.IsValid);
        Assert.Contains("attributes.size", result.Errors.Keys);
    }

    [Fact]
    public async Task Rejects_an_inactive_option()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(
            ("size", "\"41\""), ("length", "2.4"), ("features", "[\"retired\"]")));

        Assert.False(result.IsValid);
        Assert.Contains("attributes.features", result.Errors.Keys);
    }

    [Fact]
    public async Task MultiSelect_is_stored_as_a_json_array()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(
            ("size", "\"41\""), ("length", "2.4"), ("features", "[\"a\",\"b\"]")));

        Assert.True(result.IsValid);

        var features = result.Canonical["features"];
        Assert.Equal(JsonValueKind.Array, features.ValueKind);
        Assert.Equal(2, features.GetArrayLength());
    }

    [Fact]
    public async Task MultiSelect_rejects_a_scalar_and_deduplicates_repeats()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var scalar = h.Validator.Validate(schema, Values(
            ("size", "\"41\""), ("length", "2.4"), ("features", "\"a\"")));

        Assert.False(scalar.IsValid);

        var duplicated = h.Validator.Validate(schema, Values(
            ("size", "\"41\""), ("length", "2.4"), ("features", "[\"a\",\"a\",\"b\"]")));

        Assert.True(duplicated.IsValid);
        Assert.Equal(2, duplicated.Canonical["features"].GetArrayLength());
    }

    [Fact]
    public async Task Select_rejects_an_array()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(
            ("size", "[\"41\"]"), ("length", "2.4")));

        Assert.False(result.IsValid);
        Assert.Contains("attributes.size", result.Errors.Keys);
    }

    [Fact]
    public async Task Boolean_accepts_a_json_boolean_or_its_string_form()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var asBool = h.Validator.Validate(schema, Values(
            ("size", "\"41\""), ("length", "2.4"), ("waterproof", "true")));
        var asText = h.Validator.Validate(schema, Values(
            ("size", "\"41\""), ("length", "2.4"), ("waterproof", "\"false\"")));

        Assert.True(asBool.IsValid);
        Assert.Equal(JsonValueKind.True, asBool.Canonical["waterproof"].ValueKind);
        Assert.True(asText.IsValid);
        Assert.Equal(JsonValueKind.False, asText.Canonical["waterproof"].ValueKind);
    }

    [Fact]
    public async Task Text_is_trimmed_and_bounded_by_max_length()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var ok = h.Validator.Validate(schema, Values(
            ("size", "\"41\""), ("length", "2.4"), ("brand_like", "\"  Shimano  \"")));

        Assert.True(ok.IsValid);
        Assert.Equal("Shimano", ok.Canonical["brand_like"].GetString());

        var tooLong = h.Validator.Validate(schema, Values(
            ("size", "\"41\""), ("length", "2.4"),
            ("brand_like", JsonSerializer.Serialize(new string('x', 61)))));

        Assert.False(tooLong.IsValid);
    }

    [Fact]
    public async Task An_empty_optional_value_is_treated_as_absent()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(
            ("size", "\"41\""), ("length", "2.4"), ("brand_like", "\"\""), ("features", "[]")));

        Assert.True(result.IsValid);
        Assert.DoesNotContain("brand_like", result.Canonical.Keys);
        Assert.DoesNotContain("features", result.Canonical.Keys);
    }

    [Fact]
    public async Task A_null_submission_only_fails_on_required_fields()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, null);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("attributes.size", result.Errors.Keys);
        Assert.Contains("attributes.length", result.Errors.Keys);
    }

    [Fact]
    public async Task Errors_use_the_field_keyed_shape_the_frontend_binds_to()
    {
        var (h, schema) = await ChildSchemaAsync();
        using var _ = h;

        var result = h.Validator.Validate(schema, Values(("length", "999")));

        Assert.All(result.Errors.Keys, key => Assert.StartsWith("attributes.", key, StringComparison.Ordinal));
        Assert.All(result.Errors.Values, messages => Assert.NotEmpty(messages));
    }
}
