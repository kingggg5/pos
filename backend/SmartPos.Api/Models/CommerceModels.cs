namespace SmartPos.Api.Models;

public interface ITenantEntity
{
	int TenantId { get; set; }
}

public interface IVersionedEntity
{
	long Version { get; set; }
}

public interface IImmutableEntity
{
}

public enum CashShiftStatus
{
	Open,
	Closed
}

public enum OrderReversalType
{
	Void,
	Refund,
	PartialRefund
}

public enum FinancialEventType
{
	Sale,
	Refund,
	Void
}

public enum LoyaltyTransactionType
{
	Earn,
	Redeem,
	EarnReversal,
	RedeemReversal,
	Adjustment
}

public enum PromotionDiscountType
{
	FixedAmount,
	Percentage
}

public sealed class CashShift : ITenantEntity, IVersionedEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public CashShiftStatus Status { get; set; } = CashShiftStatus.Open;
	public int? OpenSlot { get; set; } = 1;
	public decimal OpeningCash { get; set; }
	public decimal CashSalesAmount { get; set; }
	public decimal CashRefundAmount { get; set; }
	public decimal ExpectedCash { get; set; }
	public decimal? ClosingCash { get; set; }
	public decimal? Difference { get; set; }
	public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
	public DateTime? ClosedAt { get; set; }
	public int OpenedByUserId { get; set; }
	public string OpenedByName { get; set; } = string.Empty;
	public int? ClosedByUserId { get; set; }
	public string? ClosedByName { get; set; }
	public string OpeningNote { get; set; } = string.Empty;
	public string ClosingNote { get; set; } = string.Empty;
	public string OpenIdempotencyKey { get; set; } = string.Empty;
	public string? CloseIdempotencyKey { get; set; }
	public long Version { get; set; }

	public Tenant? Tenant { get; set; }
	public ICollection<Order> Orders { get; set; } = [];
	public ICollection<OrderReversal> Reversals { get; set; } = [];
}

public sealed class Customer : ITenantEntity, IVersionedEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public string Phone { get; set; } = string.Empty;
	public string PhoneNormalized { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? Email { get; set; }
	public int PointsBalance { get; set; }
	public int LifetimePointsEarned { get; set; }
	public int LifetimePointsRedeemed { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public long Version { get; set; }

	public Tenant? Tenant { get; set; }
	public ICollection<Order> Orders { get; set; } = [];
	public ICollection<LoyaltyTransaction> LoyaltyTransactions { get; set; } = [];
}

public sealed class LoyaltyTransaction : ITenantEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public int CustomerId { get; set; }
	public int? OrderId { get; set; }
	public LoyaltyTransactionType Type { get; set; }
	public int PointsChange { get; set; }
	public int BalanceAfter { get; set; }
	public string Description { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	public Tenant? Tenant { get; set; }
	public Customer? Customer { get; set; }
	public Order? Order { get; set; }
}

public sealed class PromotionCoupon : ITenantEntity, IVersionedEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public string Code { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public PromotionDiscountType DiscountType { get; set; }
	public decimal Value { get; set; }
	public decimal MinimumOrderAmount { get; set; }
	public decimal? MaximumDiscountAmount { get; set; }
	public int? UsageLimit { get; set; }
	public int UsageCount { get; set; }
	public DateTime? ValidFrom { get; set; }
	public DateTime? ValidUntil { get; set; }
	public bool IsActive { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public long Version { get; set; }

	public Tenant? Tenant { get; set; }
	public ICollection<CouponRedemption> Redemptions { get; set; } = [];
}

public sealed class CouponRedemption : ITenantEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public int PromotionCouponId { get; set; }
	public int OrderId { get; set; }
	public int? CustomerId { get; set; }
	public decimal DiscountAmount { get; set; }
	public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;
	public DateTime? ReversedAt { get; set; }

	public Tenant? Tenant { get; set; }
	public PromotionCoupon? PromotionCoupon { get; set; }
	public Order? Order { get; set; }
	public Customer? Customer { get; set; }
}

public sealed class OrderReversal : ITenantEntity, IImmutableEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public int OrderId { get; set; }
	public OrderReversalType Type { get; set; }
	public decimal Amount { get; set; }
	public decimal SubTotalAmount { get; set; }
	public decimal ManualDiscountAmount { get; set; }
	public decimal CouponDiscountAmount { get; set; }
	public decimal LoyaltyDiscountAmount { get; set; }
	public decimal ServiceChargeAmount { get; set; }
	public decimal VatAmount { get; set; }
	public bool StockRestored { get; set; }
	public bool IsFullOrderReversal { get; set; }
	public string Reason { get; set; } = string.Empty;
	public string IdempotencyKey { get; set; } = string.Empty;
	public string RequestFingerprint { get; set; } = string.Empty;
	public int PerformedByUserId { get; set; }
	public string PerformedBy { get; set; } = string.Empty;
	public int? CashShiftId { get; set; }
	public int LoyaltyPointsEarnedReversed { get; set; }
	public int LoyaltyPointsRedeemedRestored { get; set; }
	public bool CouponUsageReleased { get; set; }
	public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

	public Tenant? Tenant { get; set; }
	public Order? Order { get; set; }
	public CashShift? CashShift { get; set; }
	public ICollection<OrderReversalItem> Items { get; set; } = [];
}

public sealed class OrderReversalItem : ITenantEntity, IImmutableEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public int OrderReversalId { get; set; }
	public int OrderItemId { get; set; }
	public int ProductId { get; set; }
	public string ProductName { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal SubTotalAmount { get; set; }
	public decimal ManualDiscountAmount { get; set; }
	public decimal CouponDiscountAmount { get; set; }
	public decimal LoyaltyDiscountAmount { get; set; }
	public decimal ServiceChargeAmount { get; set; }
	public decimal VatAmount { get; set; }
	public decimal TotalAmount { get; set; }

	public OrderReversal? OrderReversal { get; set; }
	public OrderItem? OrderItem { get; set; }
	public Product? Product { get; set; }
}

public sealed class FinancialEvent : ITenantEntity, IImmutableEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public FinancialEventType Type { get; set; }
	public int OrderId { get; set; }
	public int? OrderReversalId { get; set; }
	public string SourceKey { get; set; } = string.Empty;
	public decimal Amount { get; set; }
	public PaymentMethod PaymentMethod { get; set; }
	public int? CashShiftId { get; set; }
	public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
	public string Description { get; set; } = string.Empty;

	public Order? Order { get; set; }
	public OrderReversal? OrderReversal { get; set; }
	public CashShift? CashShift { get; set; }
}
