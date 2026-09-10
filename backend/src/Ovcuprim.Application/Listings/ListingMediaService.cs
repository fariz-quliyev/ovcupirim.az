using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings;

/// <summary>An upload as it arrives from the transport, without any ASP.NET types leaking in here.</summary>
public sealed record ListingMediaUpload(Stream Content, string FileName, string DeclaredContentType, long Length);

public interface IListingMediaService
{
    Task<Result<ListingMediaDto>> AddAsync(Guid listingId, ListingMediaUpload upload, CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(Guid listingId, Guid mediaId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ListingMediaDto>>> ReorderAsync(Guid listingId, IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default);
}

public sealed class ListingMediaService(
    IAppDbContext db,
    IFileStorage storage,
    IImageProcessor processor,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IListingMediaService
{
    /// <summary>Approved Phase 0 limits.</summary>
    public const int MaxImagesPerListing = 10;

    public const long MaxBytesPerImage = 5 * 1024 * 1024;

    /// <summary>
    /// B-3 (Option A): HEIC/HEIF is off the accepted upload contract. ImageSharp ships no HEIC
    /// decoder and adding one is a separate licensing decision (see IImageProcessor's remarks) — so
    /// a HEIC upload was always going to fail, and doing that after a full upload round-trip instead
    /// of at the door was the worse experience, not a more permissive one. iOS's own share sheet
    /// already offers "Most Compatible" (JPEG) for exactly this situation.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    /// <summary>
    /// Offset applied while renumbering. IX_ListingMedia_ListingId_SortOrder is UNIQUE and not
    /// deferrable, so the order is written in two passes and never collides mid-statement.
    /// </summary>
    private const int ReorderOffset = 1000;

    public async Task<Result<ListingMediaDto>> AddAsync(
        Guid listingId, ListingMediaUpload upload, CancellationToken cancellationToken = default)
    {
        var found = await LoadOwnedAsync(listingId, cancellationToken);

        if (!found.Succeeded)
        {
            return Result<ListingMediaDto>.From(found);
        }

        var listing = found.Value!;

        if (!ListingStateMachine.CanEdit(listing.Status))
        {
            return Result<ListingMediaDto>.Conflict("Bu statusda elana şəkil əlavə etmək mümkün deyil.");
        }

        if (listing.Media.Count >= MaxImagesPerListing)
        {
            return Result<ListingMediaDto>.Invalid("file", $"Ən çox {MaxImagesPerListing} şəkil əlavə edə bilərsiniz.");
        }

        if (upload.Length <= 0)
        {
            return Result<ListingMediaDto>.Invalid("file", "Fayl boşdur.");
        }

        if (upload.Length > MaxBytesPerImage)
        {
            return Result<ListingMediaDto>.Invalid("file", "Şəklin ölçüsü 5 MB-dan çox ola bilməz.");
        }

        if (!AllowedContentTypes.Contains(upload.DeclaredContentType))
        {
            return Result<ListingMediaDto>.Invalid("file", "Yalnız JPEG, PNG və WebP şəkilləri qəbul olunur.");
        }

        // The declared type is only a hint. What the bytes actually are decides.
        var sniffed = await ImageSignature.DetectAsync(upload.Content, cancellationToken);

        if (sniffed is ImageSignatureKind.Unknown)
        {
            return Result<ListingMediaDto>.Invalid("file", "Fayl şəkil deyil.");
        }

        var processed = await processor.ProcessAsync(upload.Content, cancellationToken);

        if (!processed.Succeeded)
        {
            return Result<ListingMediaDto>.Invalid("file", processed.Failure switch
            {
                // Reachable only when the declared content type lied — HEIC is no longer accepted
                // at all, but the bytes are sniffed regardless of what a client claims they are.
                ImageProcessingFailure.UnsupportedFormat =>
                    "Bu fayl HEIC formatındadır və qəbul olunmur. Zəhmət olmasa JPEG, PNG və ya WebP yükləyin.",
                ImageProcessingFailure.OutOfBounds => "Şəklin ölçüləri qəbul edilən hüdudlardan kənardır.",
                _ => "Şəkli emal etmək mümkün olmadı."
            });
        }

        var set = processed.Value!;
        var now = clock.UtcNow;

        // A random key: the user's filename never reaches storage, so it cannot steer the path.
        var baseKey = $"listings/{listing.Id:N}/{Guid.CreateVersion7():N}";
        var masterKey = $"{baseKey}{set.FileExtension}";

        await SaveRenditionAsync(masterKey, set.Master, set.ContentType, cancellationToken);

        var variants = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var variant in set.Variants)
        {
            var key = $"{baseKey}_{variant.Name}{set.FileExtension}";
            await SaveRenditionAsync(key, variant, set.ContentType, cancellationToken);
            variants[variant.Name] = key;
        }

        var media = new ListingMedia
        {
            Id = Guid.CreateVersion7(),
            ListingId = listing.Id,
            StorageKey = masterKey,
            ContentType = set.ContentType,
            Width = set.Width,
            Height = set.Height,
            SizeBytes = set.Master.Content.LongLength,
            SortOrder = listing.Media.Count == 0 ? 0 : listing.Media.Max(m => m.SortOrder) + 1,
            IsPrimary = listing.Media.Count == 0,
            Variants = variants,
            CreatedAt = now
        };

        db.ListingMedia.Add(media);
        await db.SaveChangesAsync(cancellationToken);

        return Result<ListingMediaDto>.Success(new ListingMediaDto(
            media.Id,
            storage.GetPublicUrl(media.StorageKey),
            variants.ToDictionary(v => v.Key, v => storage.GetPublicUrl(v.Value)),
            media.Width,
            media.Height,
            media.SizeBytes,
            media.SortOrder,
            media.IsPrimary));
    }

    public async Task<Result> RemoveAsync(Guid listingId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var found = await LoadOwnedAsync(listingId, cancellationToken);

        if (!found.Succeeded)
        {
            return found;
        }

        var listing = found.Value!;

        if (!ListingStateMachine.CanEdit(listing.Status))
        {
            return Result.Conflict("Bu statusda elandan şəkil silmək mümkün deyil.");
        }

        var media = listing.Media.FirstOrDefault(m => m.Id == mediaId);

        if (media is null)
        {
            return Result.NotFound("Şəkil tapılmadı.");
        }

        var wasPrimary = media.IsPrimary;

        // Clear the flag before deleting so the partial unique index never sees two primaries.
        media.IsPrimary = false;
        db.ListingMedia.Remove(media);

        if (wasPrimary)
        {
            var next = listing.Media
                .Where(m => m.Id != mediaId)
                .OrderBy(m => m.SortOrder)
                .FirstOrDefault();

            if (next is not null)
            {
                next.IsPrimary = true;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var key in media.Variants.Values.Append(media.StorageKey))
        {
            await storage.DeleteAsync(key, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ListingMediaDto>>> ReorderAsync(
        Guid listingId, IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        var found = await LoadOwnedAsync(listingId, cancellationToken);

        if (!found.Succeeded)
        {
            return Result<IReadOnlyList<ListingMediaDto>>.From(found);
        }

        var listing = found.Value!;

        if (!ListingStateMachine.CanEdit(listing.Status))
        {
            return Result<IReadOnlyList<ListingMediaDto>>.Conflict("Bu statusda şəkillərin sırasını dəyişmək mümkün deyil.");
        }

        var current = listing.Media.ToDictionary(m => m.Id);

        if (mediaIds.Count != current.Count || mediaIds.Distinct().Count() != mediaIds.Count
            || mediaIds.Any(id => !current.ContainsKey(id)))
        {
            return Result<IReadOnlyList<ListingMediaDto>>.Invalid(
                "mediaIds", "Sıralama elanın bütün şəkillərini tam olaraq göstərməlidir.");
        }

        // Pass one: move every row out of the way of the target values.
        for (var i = 0; i < mediaIds.Count; i++)
        {
            current[mediaIds[i]].SortOrder = ReorderOffset + i;
            current[mediaIds[i]].IsPrimary = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Pass two: settle on the final order, with the first image as the cover.
        for (var i = 0; i < mediaIds.Count; i++)
        {
            var media = current[mediaIds[i]];
            media.SortOrder = i;
            media.IsPrimary = i == 0;
        }

        await db.SaveChangesAsync(cancellationToken);

        var ordered = listing.Media
            .OrderBy(m => m.SortOrder)
            .Select(m => new ListingMediaDto(
                m.Id,
                storage.GetPublicUrl(m.StorageKey),
                m.Variants.ToDictionary(v => v.Key, v => storage.GetPublicUrl(v.Value)),
                m.Width,
                m.Height,
                m.SizeBytes,
                m.SortOrder,
                m.IsPrimary))
            .ToList();

        return Result<IReadOnlyList<ListingMediaDto>>.Success(ordered);
    }

    private async Task SaveRenditionAsync(
        string key, ImageRendition rendition, string contentType, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(rendition.Content, writable: false);
        await storage.SaveAsync(stream, key, contentType, cancellationToken);
    }

    private async Task<Result<Listing>> LoadOwnedAsync(Guid listingId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<Listing>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var listing = await db.Listings
            .Include(l => l.Media)
            .FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken);

        if (listing is null)
        {
            return Result<Listing>.NotFound(ListingService.NotFoundMessage);
        }

        var isStaff = currentUser.IsInRole("Admin") || currentUser.IsInRole("Moderator");

        return listing.UserId == userId || isStaff
            ? Result<Listing>.Success(listing)
            : Result<Listing>.NotFound(ListingService.NotFoundMessage);
    }
}
