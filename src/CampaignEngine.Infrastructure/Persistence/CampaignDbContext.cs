using System.Linq.Expressions;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CampaignEngine.Infrastructure.Persistence;

/// <summary>
/// The platform database. Every <see cref="ITenantOwned"/> entity is filtered to the current tenant
/// and stamped with it on insert (ADR 0005); services never filter by tenant themselves.
/// </summary>
public sealed class CampaignDbContext(DbContextOptions<CampaignDbContext> options, ITenantContext tenant) : DbContext(options)
{
    public DbSet<OrganizationRecord> Organizations => Set<OrganizationRecord>();

    public DbSet<ApiKeyRecord> ApiKeys => Set<ApiKeyRecord>();

    public DbSet<UserRecord> Users => Set<UserRecord>();

    public DbSet<MembershipRecord> Memberships => Set<MembershipRecord>();

    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();

    public DbSet<CampaignRecord> Campaigns => Set<CampaignRecord>();

    public DbSet<ProductListRecord> ProductLists => Set<ProductListRecord>();

    public DbSet<ProductListItemRecord> ProductListItems => Set<ProductListItemRecord>();

    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();

    public DbSet<RedemptionRecord> Redemptions => Set<RedemptionRecord>();

    public DbSet<CampaignUsageRecord> CampaignUsage => Set<CampaignUsageRecord>();

    public DbSet<WebhookSubscriptionRecord> WebhookSubscriptions => Set<WebhookSubscriptionRecord>();

    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    /// <summary>Read by the global query filters; EF Core re-evaluates it for every query of this instance.</summary>
    private Guid? CurrentTenantId => tenant.TenantId;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Stamps new rows with the current tenant and refuses to touch rows of another tenant.</summary>
    private void EnforceTenant()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            if (tenant.IsSystem)
            {
                if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                {
                    throw new InvalidOperationException($"System code must set TenantId explicitly on new {entry.Metadata.ClrType.Name} rows.");
                }

                continue;
            }

            var current = tenant.TenantId ?? throw new TenantRequiredException();
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = current;
            }

            if (entry.Entity.TenantId != current)
            {
                throw new InvalidOperationException(
                    $"Refusing to write a {entry.Metadata.ClrType.Name} row of another tenant.");
            }
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrganizationRecord>(e =>
        {
            e.ToTable("organizations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Slug).HasMaxLength(64);
            e.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<UserRecord>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<MembershipRecord>(e =>
        {
            e.ToTable("memberships");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionRecord>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiKeyRecord>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Prefix).HasMaxLength(16);
            e.Property(x => x.KeyHash).HasMaxLength(64);
            e.Property(x => x.Scopes).HasMaxLength(64);
            e.HasIndex(x => x.KeyHash).IsUnique();
        });

        modelBuilder.Entity<CampaignRecord>(e =>
        {
            e.ToTable("campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status, x.EndsAt });
        });

        modelBuilder.Entity<ProductListRecord>(e =>
        {
            e.ToTable("product_lists");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductListItemRecord>(e =>
        {
            e.ToTable("product_list_items");
            e.HasKey(x => new { x.ListId, x.Sku });
            e.Property(x => x.Sku).HasMaxLength(128);
        });

        modelBuilder.Entity<TransactionRecord>(e =>
        {
            e.ToTable("transactions");
            e.HasKey(x => x.Id);
            e.Property(x => x.TransactionId).HasMaxLength(128);
            e.Property(x => x.Client).HasMaxLength(128);
            e.Property(x => x.CustomerId).HasMaxLength(128);
            e.Property(x => x.Channel).HasMaxLength(64);
            e.Property(x => x.StoreId).HasMaxLength(64);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasMany(x => x.Redemptions).WithOne().HasForeignKey(x => x.TransactionRecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.TransactionId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        modelBuilder.Entity<RedemptionRecord>(e =>
        {
            e.ToTable("redemptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.TransactionId).HasMaxLength(128);
            e.Property(x => x.CampaignCode).HasMaxLength(64);
            e.Property(x => x.CustomerId).HasMaxLength(128);
            e.Property(x => x.CouponCode).HasMaxLength(64);
            e.HasIndex(x => new { x.TransactionRecordId, x.CampaignId }).IsUnique();
            e.HasIndex(x => new { x.CampaignId, x.CustomerId, x.Status });
            e.HasIndex(x => new { x.CampaignId, x.CouponCode, x.Status });
        });

        modelBuilder.Entity<CampaignUsageRecord>(e =>
        {
            e.ToTable("campaign_usage");
            e.HasKey(x => x.CampaignId);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<WebhookSubscriptionRecord>(e =>
        {
            e.ToTable("webhook_subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.Property(x => x.Secret).HasMaxLength(256);
            e.Property(x => x.Events).HasMaxLength(1024);
            e.Property(x => x.Description).HasMaxLength(256);
            e.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<OutboxMessageRecord>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(64);
            e.Property(x => x.LastError).HasMaxLength(1024);
            e.HasIndex(x => new { x.DeliveredAt, x.Failed, x.NextAttemptAt });
            e.HasIndex(x => new { x.SubscriptionId, x.CreatedAt });
            e.HasOne<WebhookSubscriptionRecord>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });

        ApplyTenantFilters(modelBuilder);
    }

    /// <summary>Adds <c>TenantId == CurrentTenantId</c> to every tenant-owned entity, plus a foreign key to its organization.</summary>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
        {
            var parameter = Expression.Parameter(entity.ClrType, "e");
            var filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Convert(Expression.Property(parameter, nameof(ITenantOwned.TenantId)), typeof(Guid?)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentTenantId))),
                parameter);
            modelBuilder.Entity(entity.ClrType).HasQueryFilter(filter);
            modelBuilder.Entity(entity.ClrType)
                .HasOne(typeof(OrganizationRecord)).WithMany().HasForeignKey(nameof(ITenantOwned.TenantId))
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>SQLite forgets DateTime.Kind; everything in the database is UTC.</summary>
    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
