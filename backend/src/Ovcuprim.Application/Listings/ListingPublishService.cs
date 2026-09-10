using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Regions;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings;

public interface IListingPublishService
{
    /// <summary>Submits a draft or a rejected listing. Never publishes directly — see Phase 4 §8.</summary>
    Task<Result<ListingDetailDto>> PublishAsync(Guid id, PublishListingRequest request, CancellationToken cancellationToken = default);

    /// <summary>Brings an expired listing back, within the 30-day window, and re-queues it.</summary>
    Task<Result<ListingDetailDto>> RestoreAsync(Guid id, PublishListingRequest request, CancellationToken cancellationToken = default);
}

public sealed class ListingPublishService(
    IAppDbContext db,
    ICategoryService categories,
    IRegionService regions,
    IAttributeValidator attributeValidator,
    IListingQuotaService quota,
    IContentScreener screener,
    ICurrentUser currentUser,
    IFileStorage storage,
    IDateTimeProvider clock) : IListingPublishService
{
    public Task<Result<ListingDetailDto>> PublishAsync(
        Guid id, PublishListingRequest request, CancellationToken cancellationToken = default) =>
        SubmitAsync(id, request, restoring: false, cancellationToken);

    public Task<Result<ListingDetailDto>> RestoreAsync(
        Guid id, PublishListingRequest request, CancellationToken cancellationToken = default) =>
        SubmitAsync(id, request, restoring: true, cancellationToken);

    private async Task<Result<ListingDetailDto>> SubmitAsync(
        Guid id, PublishListingRequest request, bool restoring, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<ListingDetailDto>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var listing = await db.Listings
            .Include(l => l.Category)
            .Include(l => l.Region)
            .Include(l => l.Media)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (listing is null || listing.UserId != userId)
        {
            return Result<ListingDetailDto>.NotFound(ListingService.NotFoundMessage);
        }

        var now = clock.UtcNow;

        if (restoring)
        {
            if (!ListingStateMachine.CanRestore(listing.Status, listing.ExpiresAt, now))
            {
                return Result<ListingDetailDto>.Conflict(
                    "Bu elanı bərpa etmək mümkün deyil. Bərpa müddəti 30 gündür.");
            }
        }
        else if (!ListingStateMachine.CanPublish(listing.Status))
        {
            return Result<ListingDetailDto>.Conflict(
                $"Bu statusda ({ListingService.StatusLabel(listing.Status)}) elan dərcə göndərilə bilməz.");
        }

        // The category and region are re-checked here, not trusted from draft time: either can have
        // been deactivated or reclassified while the seller was still filling the form.
        var schemaResult = await categories.GetSchemaAsync(listing.Category.Slug, cancellationToken);

        if (!schemaResult.Succeeded)
        {
            return Result<ListingDetailDto>.Invalid("categorySlug", "Kateqoriya artıq mövcud deyil.");
        }

        var schema = schemaResult.Value!;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!schema.Category.IsLeaf)
        {
            errors["categorySlug"] = ["Elan yalnız alt kateqoriyaya yerləşdirilə bilər."];
        }

        var regionValid = await regions.ValidateListingRegionAsync(listing.RegionId, cancellationToken);

        if (!regionValid.Succeeded)
        {
            errors["regionSlug"] = [regionValid.Message ?? "Region düzgün deyil."];
        }

        var attributes = attributeValidator.Validate(schema.Attributes, listing.Attributes);

        if (!attributes.IsValid)
        {
            foreach (var error in attributes.Errors)
            {
                errors[error.Key] = error.Value;
            }
        }

        if (listing.Media.Count == 0)
        {
            errors["media"] = ["Ən azı bir şəkil əlavə edin."];
        }

        // Restricted and Unclassified are both gated. Unclassified means the classification is still
        // pending — it is handled conservatively but never described as legally restricted.
        if (schema.Category.RequiresAgeConfirmation && listing.AgeConfirmedAt is null && !request.AgeConfirmed)
        {
            errors["ageConfirmed"] = ["Bu kateqoriya üçün yaş təsdiqi tələb olunur."];
        }

        if (errors.Count > 0)
        {
            return Result<ListingDetailDto>.Invalid(errors);
        }

        var consumed = await quota.TryConsumeAsync(userId, listing.CategoryId, cancellationToken);

        if (!consumed.Succeeded)
        {
            return Result<ListingDetailDto>.From(consumed);
        }

        var screening = await screener.ScreenAsync(
            new ListingScreeningInput(listing.Id, userId, listing.CategoryId, listing.Title, listing.Description, listing.Price),
            cancellationToken);

        if (request.AgeConfirmed && listing.AgeConfirmedAt is null)
        {
            listing.AgeConfirmedAt = now;
        }

        // Rule 6: nothing goes live without a moderator, whatever the screener said.
        listing.Status = ListingStatus.PendingModeration;
        listing.RejectionReason = null;
        listing.Attributes = attributes.Canonical;

        if (restoring)
        {
            listing.ExpiresAt = null;
        }

        if (!screening.IsClean)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.CreateVersion7(),
                ActorUserId = userId,
                Action = "listing.screening.flagged",
                EntityType = nameof(Listing),
                EntityId = listing.Id.ToString(),
                PayloadJson = AuditPayload.Flags(screening.Flags.Select(f => (f.Code, f.Message))),
                CreatedAt = now
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ListingDetailDto>.Conflict("Elan başqa yerdə dəyişdirilib. Səhifəni yeniləyib yenidən cəhd edin.");
        }

        return Result<ListingDetailDto>.Success(ListingMapper.ToDetail(listing, schema, storage, now));
    }
}
