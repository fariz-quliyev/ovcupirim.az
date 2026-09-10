using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Users;

public interface IUserService
{
    Task<Result<UserDto>> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Administrative listing. Authorization is enforced at the endpoint.</summary>
    Task<PagedResult<UserDto>> SearchAsync(string? query, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants or revokes the Moderator role (PD-7.2).
    /// </summary>
    /// <remarks>
    /// Deliberately the only role this API can change. Admin is granted out of band, so a
    /// compromised admin token cannot mint another administrator, and an administrator cannot
    /// demote themselves and lock the panel. Every change is audited.
    /// </remarks>
    Task<Result<UserDto>> SetModeratorAsync(
        Guid userId, bool isModerator, CancellationToken cancellationToken = default);
}

public sealed class UserService(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock) : IUserService
{
    public async Task<Result<UserDto>> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is null
            ? Result<UserDto>.NotFound("İstifadəçi tapılmadı.")
            : Result<UserDto>.Success(AuthService.ToDto(user));
    }

    public async Task<Result<UserDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result<UserDto>.NotFound("İstifadəçi tapılmadı.");
        }

        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();

        if (email is not null)
        {
            var taken = await db.Users.AnyAsync(u => u.Email == email && u.Id != userId, cancellationToken);
            if (taken)
            {
                return Result<UserDto>.Conflict("Bu e-mail ünvanı artıq istifadə olunur.");
            }
        }

        user.FullName = request.FullName.Trim();
        user.Email = email;
        user.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Result<UserDto>.Success(AuthService.ToDto(user));
    }

    public async Task<PagedResult<UserDto>> SearchAsync(string? query, PageRequest page, CancellationToken cancellationToken = default)
    {
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            // Provider-neutral case-insensitive match: translates to lower(...) LIKE on PostgreSQL.
            var term = query.Trim().ToLowerInvariant();
            users = users.Where(u => u.FullName.ToLower().Contains(term) || u.PhoneNumber.Contains(term));
        }

        var total = await users.CountAsync(cancellationToken);

        var items = await users
            .OrderByDescending(u => u.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(u => new UserDto(
                u.Id,
                u.PhoneNumber,
                u.FullName,
                u.Email,
                u.Role == UserRole.Admin ? "Admin" : u.Role == UserRole.Moderator ? "Moderator" : "User",
                u.IsPhoneVerified,
                u.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<UserDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<Result<UserDto>> SetModeratorAsync(
        Guid userId, bool isModerator, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result<UserDto>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        // An administrator who demotes themselves locks everyone out of the panel.
        if (actorId == userId)
        {
            return Result<UserDto>.Invalid("userId", "Öz rolunuzu dəyişə bilməzsiniz.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || user.DeletedAt is not null)
        {
            return Result<UserDto>.NotFound("İstifadəçi tapılmadı.");
        }

        // Admin is granted out of band and is never touched from here — in either direction.
        if (user.Role == UserRole.Admin)
        {
            return Result<UserDto>.Invalid("role", "Administrator rolu bu interfeysdən dəyişdirilmir.");
        }

        var target = isModerator ? UserRole.Moderator : UserRole.User;

        if (user.Role == target)
        {
            return Result<UserDto>.Success(ToDto(user));
        }

        var previous = user.Role;
        user.Role = target;

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = actorId,
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            Action = isModerator ? "user.moderator.granted" : "user.moderator.revoked",
            PayloadJson = AuditPayload.From(new { from = previous.ToString(), to = target.ToString() }),
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        return Result<UserDto>.Success(ToDto(user));
    }

    private static UserDto ToDto(User user) => new(
        user.Id,
        user.PhoneNumber,
        user.FullName,
        user.Email,
        user.Role.ToString(),
        user.IsPhoneVerified,
        user.CreatedAt);
}
