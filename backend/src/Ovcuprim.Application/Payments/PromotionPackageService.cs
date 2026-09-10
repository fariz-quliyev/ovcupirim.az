using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Application.Payments;

public interface IPromotionPackageService
{
    /// <summary>Active packages, in display order. What a seller is offered to buy.</summary>
    Task<IReadOnlyList<PromotionPackageDto>> GetActiveAsync(CancellationToken cancellationToken = default);
}

public interface IPromotionPackageAdminService
{
    /// <summary>Every package, including inactive ones the public catalog hides.</summary>
    Task<IReadOnlyList<AdminPromotionPackageDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<AdminPromotionPackageDto>> CreateAsync(
        CreatePromotionPackageRequest request, CancellationToken cancellationToken = default);

    Task<Result<AdminPromotionPackageDto>> UpdateAsync(
        int id, UpdatePromotionPackageRequest request, CancellationToken cancellationToken = default);
}

public sealed class PromotionPackageService(IAppDbContext db) : IPromotionPackageService
{
    public async Task<IReadOnlyList<PromotionPackageDto>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await db.PromotionPackages.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .Select(p => new PromotionPackageDto(
                p.Id, p.Code, p.NameAz, p.DescriptionAz, p.DurationDays, p.PriceAzn, p.Currency,
                PromotionStateMachine.BumpIntervalHours))
            .ToListAsync(cancellationToken);
}

public sealed class PromotionPackageAdminService(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    : IPromotionPackageAdminService
{
    public async Task<IReadOnlyList<AdminPromotionPackageDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var packages = await db.PromotionPackages.AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ToListAsync(cancellationToken);

        return packages.Select(ToAdminDto).ToList();
    }

    public async Task<Result<AdminPromotionPackageDto>> CreateAsync(
        CreatePromotionPackageRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();

        if (!AzerbaijaniText.IsSlug(code))
        {
            return Result<AdminPromotionPackageDto>.Invalid("code", "Kod yalnız kiçik hərf, rəqəm və defisdən ibarət ola bilər.");
        }

        if (await db.PromotionPackages.AnyAsync(p => p.Code == code, cancellationToken))
        {
            return Result<AdminPromotionPackageDto>.Conflict("Bu kodla paket artıq var.");
        }

        var now = clock.UtcNow;

        var package = new PromotionPackage
        {
            Code = code,
            NameAz = request.NameAz.Trim(),
            DescriptionAz = string.IsNullOrWhiteSpace(request.DescriptionAz) ? null : request.DescriptionAz.Trim(),
            DurationDays = request.DurationDays,
            PriceAzn = request.PriceAzn,
            IsActive = true,
            SortOrder = request.SortOrder,
            CreatedAt = now
        };

        db.PromotionPackages.Add(package);

        PaymentAuditTrail.RecordAudit(
            db, currentUser.UserId, nameof(PromotionPackage), package.Code,
            "promotion_package.created", AuditPayload.Named(package.NameAz, package.Code), now);

        await db.SaveChangesAsync(cancellationToken);

        return Result<AdminPromotionPackageDto>.Success(ToAdminDto(package));
    }

    public async Task<Result<AdminPromotionPackageDto>> UpdateAsync(
        int id, UpdatePromotionPackageRequest request, CancellationToken cancellationToken = default)
    {
        var package = await db.PromotionPackages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (package is null)
        {
            return Result<AdminPromotionPackageDto>.NotFound("Paket tapılmadı.");
        }

        var now = clock.UtcNow;

        package.NameAz = request.NameAz.Trim();
        package.DescriptionAz = string.IsNullOrWhiteSpace(request.DescriptionAz) ? null : request.DescriptionAz.Trim();
        package.DurationDays = request.DurationDays;
        package.PriceAzn = request.PriceAzn;
        package.IsActive = request.IsActive;
        package.SortOrder = request.SortOrder;

        PaymentAuditTrail.RecordAudit(
            db, currentUser.UserId, nameof(PromotionPackage), package.Code,
            "promotion_package.updated", AuditPayload.Named(package.NameAz, package.Code), now);

        await db.SaveChangesAsync(cancellationToken);

        return Result<AdminPromotionPackageDto>.Success(ToAdminDto(package));
    }

    private static AdminPromotionPackageDto ToAdminDto(PromotionPackage p) => new(
        p.Id, p.Code, p.NameAz, p.DescriptionAz, p.Type.ToString(), p.DurationDays, p.PriceAzn, p.Currency,
        p.IsActive, p.SortOrder);
}
