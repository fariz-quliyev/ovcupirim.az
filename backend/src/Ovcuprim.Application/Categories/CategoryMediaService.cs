using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Application.Categories;

public interface ICategoryMediaService
{
    Task<Result<CategoryDetailDto>> ReplaceAsync(
        int categoryId, ListingMediaUpload upload, CancellationToken cancellationToken = default);

    Task<Result<CategoryDetailDto>> RemoveAsync(int categoryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The picture on a category tile.
/// </summary>
/// <remarks>
/// The same pipeline as listing and storefront images — declared type, magic bytes, decode bounds,
/// EXIF stripped, re-encoded, a storage key this service chooses rather than the uploaded filename.
/// An administrator is a trusted user but an upload is still an upload, and the reason to re-encode
/// is what is hidden in the file, not who sent it.
///
/// Two things differ from the storefront's version. The key carries a hash of the bytes, because
/// /uploads is served immutable for a year: overwriting a path would leave every browser and cache
/// showing the old picture until long after anyone remembered changing it. And the taxonomy cache
/// is invalidated on the way out, since the tree is held in process for an hour and a picture that
/// only appears after a restart is indistinguishable from one that did not save.
/// </remarks>
public sealed class CategoryMediaService(
    IAppDbContext db,
    IFileStorage storage,
    IImageProcessor processor,
    ICategoryService categories,
    ITaxonomyCache cache,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : ICategoryMediaService
{
    /// <summary>Same ceiling as every other image the site accepts.</summary>
    public const long MaxBytes = ListingMediaService.MaxBytesPerImage;

    /// <summary>Everything category pictures are written under, for the orphan sweep.</summary>
    public const string StoragePrefix = "categories";

    public async Task<Result<CategoryDetailDto>> ReplaceAsync(
        int categoryId, ListingMediaUpload upload, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return Result<CategoryDetailDto>.NotFound("Kateqoriya tapılmadı.");
        }

        if (upload.Length <= 0)
        {
            return Result<CategoryDetailDto>.Invalid("file", "Fayl boşdur.");
        }

        if (upload.Length > MaxBytes)
        {
            return Result<CategoryDetailDto>.Invalid("file", "Şəklin ölçüsü 5 MB-dan çox ola bilməz.");
        }

        if (!ListingMediaService.AllowedContentTypes.Contains(upload.DeclaredContentType))
        {
            return Result<CategoryDetailDto>.Invalid("file", "Yalnız JPEG, PNG və WebP şəkilləri qəbul olunur.");
        }

        // The declared type is a hint; the bytes decide.
        if (await ImageSignature.DetectAsync(upload.Content, cancellationToken) is ImageSignatureKind.Unknown)
        {
            return Result<CategoryDetailDto>.Invalid("file", "Fayl şəkil deyil.");
        }

        var processed = await processor.ProcessAsync(upload.Content, cancellationToken);

        if (!processed.Succeeded)
        {
            return Result<CategoryDetailDto>.Invalid("file", processed.Failure switch
            {
                ImageProcessingFailure.UnsupportedFormat =>
                    "Bu fayl HEIC formatındadır və qəbul olunmur. Zəhmət olmasa JPEG, PNG və ya WebP yükləyin.",
                ImageProcessingFailure.OutOfBounds => "Şəklin ölçüləri qəbul edilən hüdudlardan kənardır.",
                _ => "Şəkli emal etmək mümkün olmadı."
            });
        }

        var set = processed.Value!;
        var previous = category.ImageKey;

        // The hash is of the bytes that are actually stored, so the same picture uploaded twice
        // lands on the same key and a different one never collides with it.
        var fingerprint = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(set.Master.Content))[..16];
        var key = $"{StoragePrefix}/{category.Slug}-{fingerprint}{set.FileExtension}";

        using (var stream = new MemoryStream(set.Master.Content, writable: false))
        {
            await storage.SaveAsync(stream, key, set.ContentType, cancellationToken);
        }

        category.ImageKey = key;
        category.UpdatedAt = clock.UtcNow;

        Audit("CategoryImageReplaced", category, new { key, previous });

        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        // Only after the new key is committed: a failure here leaves a file nothing points at,
        // which the orphan sweep collects, rather than a category pointing at a file that is gone.
        // Skipped when the bytes were identical, since that key is the one just committed.
        if (previous is not null && previous != key)
        {
            await TryDeleteAsync(previous, cancellationToken);
        }

        return await categories.GetBySlugAsync(category.Slug, cancellationToken);
    }

    public async Task<Result<CategoryDetailDto>> RemoveAsync(
        int categoryId, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return Result<CategoryDetailDto>.NotFound("Kateqoriya tapılmadı.");
        }

        var previous = category.ImageKey;

        if (previous is null)
        {
            return await categories.GetBySlugAsync(category.Slug, cancellationToken);
        }

        category.ImageKey = null;
        category.UpdatedAt = clock.UtcNow;

        Audit("CategoryImageRemoved", category, new { previous });

        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        await TryDeleteAsync(previous, cancellationToken);

        return await categories.GetBySlugAsync(category.Slug, cancellationToken);
    }

    /// <summary>
    /// Best-effort cleanup. A storage delete that fails must not turn a committed database write
    /// into an error the caller sees: the file is under the swept prefix, so the maintenance pass
    /// collects it once it is older than the grace period.
    /// </summary>
    private async Task TryDeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(key, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Left to the orphan sweep.
        }
    }

    private void Audit(string action, Category category, object payload) =>
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = currentUser.UserId,
            EntityType = nameof(Category),
            EntityId = category.Slug,
            Action = action,
            PayloadJson = AuditPayload.From(payload),
            CreatedAt = clock.UtcNow
        });
}
