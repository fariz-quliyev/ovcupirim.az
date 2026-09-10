namespace Ovcuprim.Domain.Entities;

/// <summary>One uploaded image. At most ten per listing; exactly one is primary.</summary>
public class ListingMedia
{
    public Guid Id { get; set; }

    public Guid ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    /// <summary>Randomised key in the storage provider — never the user's filename.</summary>
    public string StorageKey { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public int Width { get; set; }

    public int Height { get; set; }

    public long SizeBytes { get; set; }

    public int SortOrder { get; set; }

    /// <summary>The card, Open Graph and search-result image.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Generated variant keys: thumb, card, detail, og.</summary>
    public Dictionary<string, string> Variants { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}
