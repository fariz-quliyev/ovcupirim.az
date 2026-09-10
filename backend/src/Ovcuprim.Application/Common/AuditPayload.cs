using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Ovcuprim.Application.Common;

/// <summary>
/// Builds the value written to <c>AuditLog.PayloadJson</c>.
/// </summary>
/// <remarks>
/// <para>
/// That column is <c>jsonb</c>. PostgreSQL rejects anything that is not valid JSON, so a plain
/// string such as a rejection reason fails the write outright — and takes the whole moderation
/// decision down with it, because the audit row and the decision are saved in one transaction.
/// Three services did exactly that, and no test caught it: the in-memory provider stores the
/// column as text and accepts anything.
/// </para>
/// <para>
/// Every audit payload therefore goes through this type. Nothing else may assign
/// <c>PayloadJson</c> directly.
/// </para>
/// </remarks>
public static class AuditPayload
{
    /// <summary>
    /// Camel-cased like every other payload on this API, and encoded so Azerbaijani letters survive
    /// as themselves. The default encoder escapes everything outside ASCII, which would turn
    /// "Ovçu Dünyası" into "Ovçu Dünyası" in a trail a person has to read.
    /// HTML-sensitive characters are still escaped.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>Serialises any payload object. Null in, null out — the column is nullable.</summary>
    public static string? From(object? payload) =>
        payload is null ? null : JsonSerializer.Serialize(payload, Options);

    /// <summary>
    /// The commonest shape: a moderator's free-text reason. An absent reason stays null rather
    /// than becoming <c>{"reason":null}</c>, so "no reason given" and "reason was empty" do not
    /// become the same row.
    /// </summary>
    public static string? Reason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : From(new { reason = reason.Trim() });

    /// <summary>
    /// A decision about a named entity. The name and slug are carried because some decisions —
    /// a rejected store application, for one — delete the row they describe, and a trail that
    /// cannot say what it was about is not a trail.
    /// </summary>
    public static string? Named(string name, string slug, string? reason = null) =>
        From(new { name, slug, reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() });

    /// <summary>Content-screening flags, kept structured so the queue can render them.</summary>
    public static string? Flags(IEnumerable<(string Code, string Message)> flags)
    {
        var items = flags.Select(f => new { code = f.Code, message = f.Message }).ToList();

        return items.Count == 0 ? null : From(new { flags = items });
    }

    /// <summary>
    /// Reads a <see cref="Flags"/> payload back as the "code: message" lines the moderation queue
    /// has always shown — the storage shape changed, the contract did not. Tolerant on purpose: a
    /// row written before payloads were structured is skipped rather than throwing inside a queue
    /// read.
    /// </summary>
    public static IReadOnlyList<string> ReadFlagMessages(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);

            if (!document.RootElement.TryGetProperty("flags", out var flags)
                || flags.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return
            [
                .. flags.EnumerateArray()
                    .Select(Describe)
                    .Where(line => line is not null)
                    .Select(line => line!)
            ];

            static string? Describe(JsonElement flag)
            {
                var code = flag.TryGetProperty("code", out var c) ? c.GetString() : null;
                var message = flag.TryGetProperty("message", out var m) ? m.GetString() : null;

                if (string.IsNullOrWhiteSpace(message))
                {
                    return string.IsNullOrWhiteSpace(code) ? null : code;
                }

                return string.IsNullOrWhiteSpace(code) ? message : $"{code}: {message}";
            }
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
