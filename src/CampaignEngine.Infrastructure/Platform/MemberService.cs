using CampaignEngine.Infrastructure.Audit;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Platform;

public sealed record Member(Guid UserId, string Email, string Name, MemberRole Role, DateTimeOffset JoinedAt);

/// <summary>Members of the current organization. Every query is tenant-filtered by the DbContext.</summary>
public sealed class MemberService(CampaignDbContext db, AuditLog audit, TimeProvider time)
{
    public async Task<IReadOnlyList<Member>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Memberships.AsNoTracking()
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .OrderBy(x => x.m.CreatedAt)
            .Select(x => new Member(x.u.Id, x.u.Email, x.u.Name, x.m.Role, new DateTimeOffset(x.m.CreatedAt, TimeSpan.Zero)))
            .ToListAsync(cancellationToken);

    /// <summary>Adds an existing account by email. People without an account register first.</summary>
    public async Task<Member> AddAsync(string email, MemberRole role, MemberRole actorRole, CancellationToken cancellationToken = default)
    {
        EnsureMayAssign(role, actorRole);
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken)
                   ?? throw new NotFoundException($"No account uses '{normalized}'. Ask them to register first, then add them.");
        if (await db.Memberships.AnyAsync(m => m.UserId == user.Id, cancellationToken))
        {
            throw new ConflictException($"{normalized} is already a member.");
        }

        var membership = new MembershipRecord { Id = Guid.NewGuid(), UserId = user.Id, Role = role, CreatedAt = time.GetUtcNow().UtcDateTime };
        db.Memberships.Add(membership);
        audit.Stage("member.added", AuditEntities.Member, user.Id.ToString(), user.Email, $"{user.Email} added as {role.ToString().ToLowerInvariant()}.");
        await db.SaveChangesAsync(cancellationToken);
        return new Member(user.Id, user.Email, user.Name, role, new DateTimeOffset(membership.CreatedAt, TimeSpan.Zero));
    }

    public async Task<Member> ChangeRoleAsync(Guid userId, MemberRole role, MemberRole actorRole, CancellationToken cancellationToken = default)
    {
        var membership = await LoadAsync(userId, cancellationToken);
        EnsureMayAssign(role, actorRole);
        EnsureMayAssign(membership.Role, actorRole); // an admin cannot change an owner
        if (membership.Role == MemberRole.Owner && role != MemberRole.Owner)
        {
            await EnsureAnotherOwnerAsync(userId, cancellationToken);
        }

        audit.Stage("member.roleChanged", AuditEntities.Member, userId.ToString(), null, $"Role changed from {membership.Role.ToString().ToLowerInvariant()} to {role.ToString().ToLowerInvariant()}.");
        membership.Role = role;
        await db.SaveChangesAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, cancellationToken);
        return new Member(user.Id, user.Email, user.Name, role, new DateTimeOffset(membership.CreatedAt, TimeSpan.Zero));
    }

    public async Task RemoveAsync(Guid userId, MemberRole actorRole, CancellationToken cancellationToken = default)
    {
        var membership = await LoadAsync(userId, cancellationToken);
        EnsureMayAssign(membership.Role, actorRole);
        if (membership.Role == MemberRole.Owner)
        {
            await EnsureAnotherOwnerAsync(userId, cancellationToken);
        }

        db.Memberships.Remove(membership);
        audit.Stage("member.removed", AuditEntities.Member, userId.ToString(), null, $"Member with role {membership.Role.ToString().ToLowerInvariant()} removed.");
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<MembershipRecord> LoadAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Memberships.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("Member was not found.");

    /// <summary>Only owners hand out or take away ownership.</summary>
    private static void EnsureMayAssign(MemberRole role, MemberRole actorRole)
    {
        if (role == MemberRole.Owner && actorRole != MemberRole.Owner)
        {
            throw new ForbiddenException("Only owners can grant, change or remove the owner role.");
        }
    }

    private async Task EnsureAnotherOwnerAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await db.Memberships.AnyAsync(m => m.Role == MemberRole.Owner && m.UserId != userId, cancellationToken))
        {
            throw new ConflictException("An organization needs at least one owner.");
        }
    }
}

/// <summary>Authenticated but not allowed (maps to 403).</summary>
public sealed class ForbiddenException(string message) : CampaignEngineException(message);
