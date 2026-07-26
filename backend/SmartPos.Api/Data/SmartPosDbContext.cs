using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;

namespace SmartPos.Api.Data;

public sealed class SmartPosDbContext(
	DbContextOptions<SmartPosDbContext> options,
	ITenantProvider tenantProvider) : DbContext(options)
{
	private int CurrentTenantId => tenantProvider.CurrentTenantId ?? -1;

	public DbSet<Tenant> Tenants => Set<Tenant>();
	public DbSet<User> Users => Set<User>();
	public DbSet<Category> Categories => Set<Category>();
	public DbSet<Product> Products => Set<Product>();
	public DbSet<Order> Orders => Set<Order>();
	public DbSet<OrderItem> OrderItems => Set<OrderItem>();
	public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
	public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
	public DbSet<CashShift> CashShifts => Set<CashShift>();
	public DbSet<Customer> Customers => Set<Customer>();
	public DbSet<LoyaltyTransaction> LoyaltyTransactions => Set<LoyaltyTransaction>();
	public DbSet<PromotionCoupon> PromotionCoupons => Set<PromotionCoupon>();
	public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
	public DbSet<OrderReversal> OrderReversals => Set<OrderReversal>();
	public DbSet<OrderReversalItem> OrderReversalItems => Set<OrderReversalItem>();
	public DbSet<FinancialEvent> FinancialEvents => Set<FinancialEvent>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.Entity<Tenant>(entity =>
		{
			entity.HasKey(t => t.Id);
			entity.HasIndex(t => t.Slug).IsUnique();
			entity.Property(t => t.BusinessTimeZoneId).HasMaxLength(100);
		});

		modelBuilder.Entity<User>(entity =>
		{
			entity.HasKey(u => u.Id);
			entity.HasIndex(u => u.Email).IsUnique();
			entity.HasQueryFilter(u => u.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<Category>(entity =>
		{
			entity.HasKey(c => c.Id);
			entity.HasQueryFilter(c => c.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<Product>(entity =>
		{
			entity.HasKey(p => p.Id);
			entity.HasIndex(p => new { p.TenantId, p.Barcode }).IsUnique();
			entity.Property(p => p.Price).HasPrecision(18, 2);
			entity.Property(p => p.Cost).HasPrecision(18, 2);
			entity.Property(p => p.Version).IsConcurrencyToken();
			entity.HasQueryFilter(p => p.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<Order>(entity =>
		{
			entity.HasKey(o => o.Id);
			entity.HasIndex(o => new { o.TenantId, o.OrderNo }).IsUnique();
			entity.HasIndex(o => new { o.TenantId, o.IdempotencyKey }).IsUnique();
			entity.Property(o => o.SubTotalAmount).HasPrecision(18, 2);
			entity.Property(o => o.ServiceChargeAmount).HasPrecision(18, 2);
			entity.Property(o => o.VatAmount).HasPrecision(18, 2);
			entity.Property(o => o.TotalAmount).HasPrecision(18, 2);
			entity.Property(o => o.DiscountAmount).HasPrecision(18, 2);
			entity.Property(o => o.ManualDiscountAmount).HasPrecision(18, 2);
			entity.Property(o => o.CouponDiscountAmount).HasPrecision(18, 2);
			entity.Property(o => o.LoyaltyDiscountAmount).HasPrecision(18, 2);
			entity.Property(o => o.PaidAmount).HasPrecision(18, 2);
			entity.Property(o => o.ChangeAmount).HasPrecision(18, 2);
			entity.Property(o => o.TotalRefundedAmount).HasPrecision(18, 2);
			entity.Property(o => o.IdempotencyKey).HasMaxLength(100);
			entity.Property(o => o.Version).IsConcurrencyToken();
			entity.HasQueryFilter(o => o.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<OrderItem>(entity =>
		{
			entity.HasKey(i => i.Id);
			entity.Property(i => i.UnitPrice).HasPrecision(18, 2);
			entity.Property(i => i.SubTotal).HasPrecision(18, 2);
			entity.HasIndex(i => new { i.TenantId, i.OrderId });
			entity.HasQueryFilter(i => i.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<StockTransaction>(entity =>
		{
			entity.HasKey(st => st.Id);
			entity.HasQueryFilter(st => st.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<AuditLog>(entity =>
		{
			entity.HasKey(a => a.Id);
			entity.HasQueryFilter(a => a.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<CashShift>(entity =>
		{
			entity.HasKey(s => s.Id);
			entity.HasIndex(s => new { s.TenantId, s.OpenSlot }).IsUnique();
			entity.HasIndex(s => new { s.TenantId, s.OpenIdempotencyKey }).IsUnique();
			entity.HasIndex(s => new { s.TenantId, s.CloseIdempotencyKey }).IsUnique();
			entity.Property(s => s.OpeningCash).HasPrecision(18, 2);
			entity.Property(s => s.CashSalesAmount).HasPrecision(18, 2);
			entity.Property(s => s.CashRefundAmount).HasPrecision(18, 2);
			entity.Property(s => s.ExpectedCash).HasPrecision(18, 2);
			entity.Property(s => s.ClosingCash).HasPrecision(18, 2);
			entity.Property(s => s.Difference).HasPrecision(18, 2);
			entity.Property(s => s.Version).IsConcurrencyToken();
			entity.HasQueryFilter(s => s.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<Customer>(entity =>
		{
			entity.HasKey(c => c.Id);
			entity.HasIndex(c => new { c.TenantId, c.PhoneNormalized }).IsUnique();
			entity.Property(c => c.Version).IsConcurrencyToken();
			entity.HasQueryFilter(c => c.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<LoyaltyTransaction>(entity =>
		{
			entity.HasKey(t => t.Id);
			entity.HasQueryFilter(t => t.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<PromotionCoupon>(entity =>
		{
			entity.HasKey(p => p.Id);
			entity.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
			entity.Property(p => p.Value).HasPrecision(18, 2);
			entity.Property(p => p.MinimumOrderAmount).HasPrecision(18, 2);
			entity.Property(p => p.MaximumDiscountAmount).HasPrecision(18, 2);
			entity.Property(p => p.Version).IsConcurrencyToken();
			entity.HasQueryFilter(p => p.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<CouponRedemption>(entity =>
		{
			entity.HasKey(r => r.Id);
			entity.HasIndex(r => new { r.TenantId, r.PromotionCouponId, r.OrderId }).IsUnique();
			entity.Property(r => r.DiscountAmount).HasPrecision(18, 2);
			entity.HasQueryFilter(r => r.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<OrderReversal>(entity =>
		{
			entity.HasKey(r => r.Id);
			entity.HasIndex(r => new { r.TenantId, r.OrderId });
			entity.HasIndex(r => new { r.TenantId, r.IdempotencyKey }).IsUnique();
			entity.Property(r => r.Amount).HasPrecision(18, 2);
			entity.Property(r => r.SubTotalAmount).HasPrecision(18, 2);
			entity.Property(r => r.ManualDiscountAmount).HasPrecision(18, 2);
			entity.Property(r => r.CouponDiscountAmount).HasPrecision(18, 2);
			entity.Property(r => r.LoyaltyDiscountAmount).HasPrecision(18, 2);
			entity.Property(r => r.ServiceChargeAmount).HasPrecision(18, 2);
			entity.Property(r => r.VatAmount).HasPrecision(18, 2);
			entity.Property(r => r.IdempotencyKey).HasMaxLength(100);
			entity.Property(r => r.RequestFingerprint).HasMaxLength(64);
			entity.HasQueryFilter(r => r.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<OrderReversalItem>(entity =>
		{
			entity.HasKey(item => item.Id);
			entity.HasIndex(item => new { item.TenantId, item.OrderReversalId, item.OrderItemId });
			entity.Property(item => item.SubTotalAmount).HasPrecision(18, 2);
			entity.Property(item => item.ManualDiscountAmount).HasPrecision(18, 2);
			entity.Property(item => item.CouponDiscountAmount).HasPrecision(18, 2);
			entity.Property(item => item.LoyaltyDiscountAmount).HasPrecision(18, 2);
			entity.Property(item => item.ServiceChargeAmount).HasPrecision(18, 2);
			entity.Property(item => item.VatAmount).HasPrecision(18, 2);
			entity.Property(item => item.TotalAmount).HasPrecision(18, 2);
			entity.HasQueryFilter(item => item.TenantId == CurrentTenantId);
		});

		modelBuilder.Entity<FinancialEvent>(entity =>
		{
			entity.HasKey(financialEvent => financialEvent.Id);
			entity.HasIndex(financialEvent => new { financialEvent.TenantId, financialEvent.SourceKey }).IsUnique();
			entity.HasIndex(financialEvent => new { financialEvent.TenantId, financialEvent.OccurredAt });
			entity.Property(financialEvent => financialEvent.SourceKey).HasMaxLength(140);
			entity.Property(financialEvent => financialEvent.Amount).HasPrecision(18, 2);
			entity.HasQueryFilter(financialEvent => financialEvent.TenantId == CurrentTenantId);
		});
	}

	public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
	{
		var currentTenantId = tenantProvider.CurrentTenantId;
		var tenantEntries = ChangeTracker.Entries<ITenantEntity>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
			.ToList();

		if (tenantEntries.Count > 0 && (!currentTenantId.HasValue || currentTenantId.Value <= 0))
		{
			throw new InvalidOperationException("A valid tenant context is required to modify tenant-owned data.");
		}

		if (currentTenantId is > 0)
		{
			foreach (var entry in tenantEntries)
			{
				if (entry.State == EntityState.Added)
				{
					entry.Entity.TenantId = currentTenantId.Value;
					continue;
				}

				var originalTenantId = entry.OriginalValues.GetValue<int>(nameof(ITenantEntity.TenantId));
				if (originalTenantId != currentTenantId.Value || entry.Entity.TenantId != currentTenantId.Value)
					throw new InvalidOperationException("Cross-tenant data modification was blocked.");
			}
		}

		foreach (var entry in ChangeTracker.Entries<IVersionedEntity>())
		{
			if (entry.State == EntityState.Added)
				entry.Entity.Version = 1;
			else if (entry.State == EntityState.Modified)
				entry.Entity.Version = Math.Max(1, entry.OriginalValues.GetValue<long>(nameof(IVersionedEntity.Version)) + 1);
		}

		if (ChangeTracker.Entries<IImmutableEntity>().Any(entry =>
			entry.State is EntityState.Modified or EntityState.Deleted))
		{
			throw new InvalidOperationException("Immutable financial ledger records cannot be modified or deleted.");
		}

		return base.SaveChangesAsync(cancellationToken);
	}
}
