using System.Security.Cryptography;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Platform;

public enum MemberRole
{
    Member,
    Admin,
    Owner,
}

/// <summary>A person who signs in to the web app. Global: one user can belong to several organizations.</summary>
public sealed class UserRecord
{
    public Guid Id { get; set; }

    /// <summary>Lower-cased, unique.</summary>
    public required string Email { get; set; }

    public required string Name { get; set; }

    public required string PasswordHash { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class MembershipRecord : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public MemberRole Role { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>A signed-in browser session (ADR 0007). Only the token hash is stored.</summary>
public sealed class SessionRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The organization the session currently acts for; null for users without memberships.</summary>
    public Guid? TenantId { get; set; }

    public required string TokenHash { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}

public sealed class AccountOptions
{
    public int SessionDays { get; set; } = 14;

    public int MinimumPasswordLength { get; set; } = 8;
}

public sealed record UserInfo(Guid Id, string Email, string Name);

public sealed record MembershipInfo(Guid OrganizationId, string OrganizationName, string OrganizationSlug, MemberRole Role);

/// <summary>What the web app needs after sign-in.</summary>
public sealed record SessionInfo(UserInfo User, MembershipInfo? Current, IReadOnlyList<MembershipInfo> Memberships, DateTimeOffset ExpiresAt);

/// <summary>A new session: <see cref="Token"/> is shown only here.</summary>
public sealed record SignInResult(string Token, SessionInfo Session);

/// <summary>The identity behind a presented session token.</summary>
public sealed record SessionIdentity(Guid SessionId, UserInfo User, Guid? TenantId, MemberRole? Role);

public sealed class IdentityService(
    CampaignDbContext db,
    IServiceScopeFactory scopes,
    OrganizationService organizations,
    IOptions<AccountOptions> options,
    TimeProvider time)
{
    private const string TokenPrefix = "qxs_";
    private static readonly PasswordHasher<UserRecord> Hasher = new();

    /// <summary>Creates a user; with an organization name the user also becomes that organization's owner.</summary>
    public async Task<SignInResult> RegisterAsync(string email, string password, string name, string? organizationName, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        var errors = new List<string>();
        if (!email.Contains('@', StringComparison.Ordinal) || email.Length > 256)
        {
            errors.Add("email is not valid.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128)
        {
            errors.Add("name is required (max 128 characters).");
        }

        if (password.Length < options.Value.MinimumPasswordLength)
        {
            errors.Add($"password must be at least {options.Value.MinimumPasswordLength} characters.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = new UserRecord { Id = Guid.NewGuid(), Email = email, Name = name.Trim(), PasswordHash = "", CreatedAt = Now() };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        Guid? organizationId = null;
        if (!string.IsNullOrWhiteSpace(organizationName))
        {
            var organization = await organizations.CreateAsync(organizationName, cancellationToken: cancellationToken);
            await AddMembershipAsync(organization.Id, user.Id, MemberRole.Owner, cancellationToken);
            organizationId = organization.Id;
        }

        return await StartSessionAsync(user, organizationId, cancellationToken);
    }

    /// <summary>Signs in; acts for <paramref name="organizationSlug"/> or the first organization of the user.</summary>
    public async Task<SignInResult> LoginAsync(string email, string password, string? organizationSlug, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            throw new AuthenticationFailedException("Email or password is incorrect.");
        }

        var memberships = await MembershipsAsync(user.Id, cancellationToken);
        var chosen = organizationSlug is null
            ? memberships.FirstOrDefault()
            : memberships.FirstOrDefault(m => string.Equals(m.OrganizationSlug, organizationSlug, StringComparison.OrdinalIgnoreCase))
              ?? throw new AuthenticationFailedException("You are not a member of that organization.");
        return await StartSessionAsync(user, chosen?.OrganizationId, cancellationToken);
    }

    public async Task LogoutAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is not null && session.RevokedAt is null)
        {
            session.RevokedAt = Now();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Resolves a presented bearer token. Runs before the tenant is known.</summary>
    public async Task<SessionIdentity?> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = ApiKeyService.Hash(token);
        var now = Now();
        var session = await db.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TokenHash == hash && s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        MemberRole? role = null;
        if (session.TenantId is { } tenantId)
        {
            // Identity lookup before the tenant context exists: filtered explicitly by tenant and user.
            role = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.UserId == user.Id)
                .Select(m => (MemberRole?)m.Role)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new SessionIdentity(session.Id, ToInfo(user), role is null ? null : session.TenantId, role);
    }

    public async Task<SessionInfo> DescribeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.Sessions.AsNoTracking().FirstAsync(s => s.Id == sessionId, cancellationToken);
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == session.UserId, cancellationToken);
        return await DescribeAsync(user, session, cancellationToken);
    }

    /// <summary>Makes the session act for another organization the user belongs to.</summary>
    public async Task<SessionInfo> SwitchOrganizationAsync(Guid sessionId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        var session = await db.Sessions.FirstAsync(s => s.Id == sessionId, cancellationToken);
        var memberships = await MembershipsAsync(session.UserId, cancellationToken);
        if (!memberships.Any(m => m.OrganizationId == organizationId))
        {
            throw new NotFoundException("You are not a member of that organization.");
        }

        session.TenantId = organizationId;
        await db.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(sessionId, cancellationToken);
    }

    /// <summary>Creates another organization owned by the signed-in user and switches the session to it.</summary>
    public async Task<SessionInfo> CreateOrganizationAsync(Guid sessionId, string name, CancellationToken cancellationToken = default)
    {
        var session = await db.Sessions.AsNoTracking().FirstAsync(s => s.Id == sessionId, cancellationToken);
        var organization = await organizations.CreateAsync(name, cancellationToken: cancellationToken);
        await AddMembershipAsync(organization.Id, session.UserId, MemberRole.Owner, cancellationToken);
        return await SwitchOrganizationAsync(sessionId, organization.Id, cancellationToken);
    }

    private async Task AddMembershipAsync(Guid organizationId, Guid userId, MemberRole role, CancellationToken cancellationToken)
    {
        // A fresh scope acting for the new organization: the current request may already act for another one.
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(organizationId);
        var scoped = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        scoped.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), UserId = userId, Role = role, CreatedAt = Now() });
        await scoped.SaveChangesAsync(cancellationToken);
    }

    private async Task<SignInResult> StartSessionAsync(UserRecord user, Guid? organizationId, CancellationToken cancellationToken)
    {
        var token = TokenPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var session = new SessionRecord
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = organizationId,
            TokenHash = ApiKeyService.Hash(token),
            CreatedAt = Now(),
            ExpiresAt = Now().AddDays(options.Value.SessionDays),
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return new SignInResult(token, await DescribeAsync(user, session, cancellationToken));
    }

    private async Task<SessionInfo> DescribeAsync(UserRecord user, SessionRecord session, CancellationToken cancellationToken)
    {
        var memberships = await MembershipsAsync(user.Id, cancellationToken);
        return new SessionInfo(
            ToInfo(user),
            memberships.FirstOrDefault(m => m.OrganizationId == session.TenantId),
            memberships,
            new DateTimeOffset(session.ExpiresAt, TimeSpan.Zero));
    }

    /// <summary>All organizations of a user — an identity query that spans tenants by design.</summary>
    private async Task<List<MembershipInfo>> MembershipsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Organizations, m => m.TenantId, o => o.Id, (m, o) => new { o.Id, o.Name, o.Slug, m.Role, m.CreatedAt })
            .OrderBy(x => x.CreatedAt)
            .Select(x => new MembershipInfo(x.Id, x.Name, x.Slug, x.Role))
            .ToListAsync(cancellationToken);

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private static string NormalizeEmail(string email) => (email ?? "").Trim().ToLowerInvariant();

    private static UserInfo ToInfo(UserRecord user) => new(user.Id, user.Email, user.Name);
}

public sealed class AuthenticationFailedException(string message) : CampaignEngineException(message);
