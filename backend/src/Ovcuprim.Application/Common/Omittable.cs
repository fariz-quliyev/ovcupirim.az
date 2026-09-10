using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ovcuprim.Application.Common;

/// <summary>
/// Distinguishes "this field was not sent" from "this field was sent as null".
/// </summary>
/// <remarks>
/// <para>
/// An ordinary <c>string?</c> collapses both cases into null, which is how a partial region import
/// silently erased coordinates it never mentioned. This type keeps them apart: the JSON reader is
/// only invoked for properties that are actually present, so a default value means the caller
/// omitted the field and <see cref="HasValue"/> means they stated it — including stating null.
/// </para>
/// <para>
/// Omitted means "leave what is already there". An explicit null means "clear it". Nothing else
/// can express the difference without a schema change or a second request shape.
/// </para>
/// </remarks>
[JsonConverter(typeof(OmittableConverterFactory))]
public readonly struct Omittable<T>
{
    private readonly T? _value;

    public Omittable(T? value)
    {
        _value = value;
        HasValue = true;
    }

    /// <summary>True when the caller sent this field at all, whatever they sent.</summary>
    public bool HasValue { get; }

    /// <summary>The supplied value, which may itself be null when the caller sent null.</summary>
    public T? Value => _value;

    /// <summary>
    /// The value to store: what the caller sent when they sent anything, otherwise what is already
    /// there. This is the whole point of the type, so callers should reach for it rather than
    /// branching on <see cref="HasValue"/> themselves.
    /// </summary>
    public T? Or(T? current) => HasValue ? _value : current;

    public static implicit operator Omittable<T>(T? value) => new(value);

    /// <summary>An omitted field, for tests and for building rows in code.</summary>
    public static Omittable<T> Absent => default;
}

/// <summary>
/// Reads and writes <see cref="Omittable{T}"/> as the bare value. Absent properties never reach a
/// converter, which is exactly what makes the distinction work.
/// </summary>
public sealed class OmittableConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Omittable<>);

    [RequiresDynamicCode("Creates a generic converter over the wrapped type.")]
    [RequiresUnreferencedCode("Creates a generic converter over the wrapped type.")]
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];

        return (JsonConverter)Activator.CreateInstance(
            typeof(OmittableConverter<>).MakeGenericType(valueType))!;
    }
}

internal sealed class OmittableConverter<T> : JsonConverter<Omittable<T>>
{
    public override Omittable<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Reached only when the property is present, so this is always a stated value — including
        // an explicit null, which reads as an Omittable carrying null.
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new Omittable<T>(default);
        }

        return new Omittable<T>(JsonSerializer.Deserialize<T>(ref reader, options));
    }

    public override void Write(Utf8JsonWriter writer, Omittable<T> value, JsonSerializerOptions options)
    {
        if (!value.HasValue || value.Value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Value, options);
    }
}
