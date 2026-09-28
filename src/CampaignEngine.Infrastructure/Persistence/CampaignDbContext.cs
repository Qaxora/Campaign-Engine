using CampaignEngine.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CampaignEngine.Infrastructure.Persistence;

public sealed class CampaignDbContext(DbContextOptions<CampaignDbContext> options) : DbContext(options)
{
    public DbSet<CampaignRecord> Campaigns => Set<CampaignRecord>();

    public DbSet<ProductListRecord> ProductLists => Set<ProductListRecord>();

    public DbSet<ProductListItemRecord> ProductListItems => Set<ProductListItemRecord>();

    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();

    public DbSet<RedemptionRecord> Redemptions => Set<RedemptionRecord>();

    public DbSet<CampaignUsageRecord> CampaignUsage => Set<CampaignUsageRecord>();

    public DbSet<WebhookSubscriptionRecord> WebhookSubscriptions => Set<WebhookSubscriptionRecord>();

    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CampaignRecord>(e =>
        {
            e.ToTable("campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.Status, x.EndsAt });
        });

        modelBuilder.Entity<ProductListRecord>(e =>
        {
            e.ToTable("product_lists");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Code).IsUnique();
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
            e.HasKey(x => x.TransactionId);
            e.Property(x => x.TransactionId).HasMaxLength(128);
            e.Property(x => x.Client).HasMaxLength(128);
            e.Property(x => x.CustomerId).HasMaxLength(128);
            e.Property(x => x.Channel).HasMaxLength(64);
            e.Property(x => x.StoreId).HasMaxLength(64);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasMany(x => x.Redemptions).WithOne().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<RedemptionRecord>(e =>
        {
            e.ToTable("redemptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.TransactionId).HasMaxLength(128);
            e.Property(x => x.CampaignCode).HasMaxLength(64);
            e.Property(x => x.CustomerId).HasMaxLength(128);
            e.Property(x => x.CouponCode).HasMaxLength(64);
            e.HasIndex(x => new { x.TransactionId, x.CampaignId }).IsUnique();
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
    }

    /// <summary>SQLite forgets DateTime.Kind; everything in the database is UTC.</summary>
    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
