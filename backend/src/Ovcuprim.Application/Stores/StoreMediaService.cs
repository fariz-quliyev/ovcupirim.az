using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;

namespace Ovcuprim.Application.Stores;

/// <summary>Which storefront image an upload is replacing.</summary>
public enum StoreImageKind
{
    Logo = 0,
    Banner = 1
}

public interface IStoreMediaService
{
    Task<Result<StoreOwnerDto>> ReplaceAsync(
        StoreImageKind kind, ListingMediaUpload upload, CancellationToken cancellationToken = default);

    Task<Result<StoreOwnerDto>> RemoveAsync(StoreImageKind kind, CancellationToken cancellationToken = default);
}

/// <summary>
/// Storefront logo and banner.
/// </summary>
/// <remarks>
/// Deliberately the same pipeline as listing images — declared type, magic bytes, decode bounds,
/// EXIF stripped, re-encoded to WebP, random storage key — because a storefront image is uploaded
/// by the same kind of user through the same kind of form. The only difference is where the key is
/// written: a column on the store rather than a media row, since ListingMedia is listing-scoped.
/// Replaced images are deleted immediately, and anything the delete misses is caught by the
/// maintenance pass's orphan reconciliation.
/// </remarks>
public sealed class StoreMediaService(
    IAppDbContext db,
    IFileStorage storage,
    IImageProcessor processor,
    ICurrentUser currentUser) : IStoreMediaService
{
    /// <summary>Same ceiling as a listing image.</summary>
    public const long MaxBytes = ListingMediaService.MaxBytesPerImage;

    /// <summary>Everything storefront images are written under, for the orphan sweep.</summary>
    public const string StoragePrefix = "stores";

    /// <summary>The same wording the store editor uses, because it is the same situation.</summary>
    private const string ConflictMessage =
        "Mağaza başqa yerdə dəyişdirilib. Səhifəni yeniləyib yenidən cəhd edin.";

    public async Task<Result<StoreOwnerDto>> ReplaceAsync(
        StoreImageKind kind, ListingMediaUpload upload, CancellationToken cancellationToken = default)
    {
        var found = await LoadMineAsync(cancellationToken);

        if (!found.Succeeded)
        {
            return Result<StoreOwnerDto>.From(found);
        }

        var store = found.Value!;

        if (upload.Length <= 0)
        {
            return Result<StoreOwnerDto>.Invalid("file", "Fayl boşdur.");
        }

        if (upload.Length > MaxBytes)
        {
            return Result<StoreOwnerDto>.Invalid("file", "Şəklin ölçüsü 5 MB-dan çox ola bilməz.");
        }

        if (!ListingMediaService.AllowedContentTypes.Contains(upload.DeclaredContentType))
        {
            return Result<StoreOwnerDto>.Invalid("file", "Yalnız JPEG, PNG və WebP şəkilləri qəbul olunur.");
        }

        // The declared type is a hint; the bytes decide.
        if (await ImageSignature.DetectAsync(upload.Content, cancellationToken) is ImageSignatureKind.Unknown)
        {
            return Result<StoreOwnerDto>.Invalid("file", "Fayl şəkil deyil.");
        }

        var processed = await processor.ProcessAsync(upload.Content, cancellationToken);

        if (!processed.Succeeded)
        {
            return Result<StoreOwnerDto>.Invalid("file", processed.Failure switch
            {
                ImageProcessingFailure.UnsupportedFormat =>
                    "Bu fayl HEIC formatındadır və qəbul olunmur. Zəhmət olmasa JPEG, PNG və ya WebP yükləyin.",
                ImageProcessingFailure.OutOfBounds => "Şəklin ölçüləri qəbul edilən hüdudlardan kənardır.",
                _ => "Şəkli emal etmək mümkün olmadı."
            });
        }

        var set = processed.Value!;
        var previous = Current(store, kind);

        // Random key, never the uploaded filename.
        var key = $"{StoragePrefix}/{store.Id:N}/{kind.ToString().ToLowerInvariant()}-{Guid.CreateVersion7():N}{set.FileExtension}";

        using (var stream = new MemoryStream(set.Master.Content, writable: false))
        {
            await storage.SaveAsync(stream, key, set.ContentType, cancellationToken);
        }

        Assign(store, kind, key);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The storefront moved under us, so this key was never committed and nothing will ever
            // reference the file just written. Delete it here rather than leaving it for the sweep;
            // if that delete fails too, the file is still inside the orphan-cleanup guarantee
            // because it sits under the same "stores/" prefix the maintenance pass reconciles.
            await TryDeleteAsync(key, cancellationToken);

            return Result<StoreOwnerDto>.Conflict(ConflictMessage);
        }

        // Only after the new key is committed, so a failure here leaves an orphan rather than a
        // storefront pointing at a file that is already gone.
        if (previous is not null)
        {
            await TryDeleteAsync(previous, cancellationToken);
        }

        return Result<StoreOwnerDto>.Success(ToOwner(store));
    }

    public async Task<Result<StoreOwnerDto>> RemoveAsync(
        StoreImageKind kind, CancellationToken cancellationToken = default)
    {
        var found = await LoadMineAsync(cancellationToken);

        if (!found.Succeeded)
        {
            return Result<StoreOwnerDto>.From(found);
        }

        var store = found.Value!;
        var previous = Current(store, kind);

        if (previous is null)
        {
            return Result<StoreOwnerDto>.Success(ToOwner(store));
        }

        Assign(store, kind, null);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The store still points at the file, so it must stay where it is.
            return Result<StoreOwnerDto>.Conflict(ConflictMessage);
        }

        await TryDeleteAsync(previous, cancellationToken);

        return Result<StoreOwnerDto>.Success(ToOwner(store));
    }

    /// <summary>
    /// Best-effort cleanup. A storage delete that fails must not turn a completed database write
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

    private async Task<Result<Domain.Entities.Store>> LoadMineAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<Domain.Entities.Store>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var store = await db.Stores.FirstOrDefaultAsync(s => s.OwnerUserId == userId, cancellationToken);

        return store is null
            ? Result<Domain.Entities.Store>.NotFound("Sizin mağazanız yoxdur.")
            : Result<Domain.Entities.Store>.Success(store);
    }

    private static string? Current(Domain.Entities.Store store, StoreImageKind kind) =>
        kind == StoreImageKind.Logo ? store.LogoStorageKey : store.BannerStorageKey;

    private static void Assign(Domain.Entities.Store store, StoreImageKind kind, string? key)
    {
        if (kind == StoreImageKind.Logo)
        {
            store.LogoStorageKey = key;
        }
        else
        {
            store.BannerStorageKey = key;
        }
    }

    private StoreOwnerDto ToOwner(Domain.Entities.Store store) => new(
        store.Id,
        store.Slug,
        store.Name,
        store.Description,
        store.Address,
        store.Phone,
        store.Status.ToString(),
        store.IsVerified,
        store.Status == Domain.Enums.StoreStatus.Active,
        store.LogoStorageKey is null ? null : storage.GetPublicUrl(store.LogoStorageKey),
        store.BannerStorageKey is null ? null : storage.GetPublicUrl(store.BannerStorageKey),
        store.ListingCount,
        store.FollowerCount,
        store.CreatedAt);
}
