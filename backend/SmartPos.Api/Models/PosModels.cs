namespace SmartPos.Api.Models;

public enum UserRole
{
	Owner,
	Manager,
	Cashier
}

public enum OrderStatus
{
	Completed,
	Cancelled,
	Refunded,
	PartiallyRefunded
}

public enum PaymentMethod
{
	Cash,
	PromptPay,
	CreditCard
}

public enum StockTransactionType
{
	Sale,
	Restock,
	Adjustment,
	VoidRestock,
	RefundRestock
}

public sealed class Tenant
{
	public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Slug { get; set; } = string.Empty;
	public string Plan { get; set; } = "Basic";
	public bool IsActive { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string? QrCodeUrl { get; set; }
	public decimal VatRate { get; set; } = 7.00m;
	public decimal ServiceChargeRate { get; set; } = 0.00m;
	public string ReceiptHeaderNote { get; set; } = "Thank you for visiting!";
	public string ReceiptFooterNote { get; set; } = "Tax Invoice / Receipt";
	public string BusinessTimeZoneId { get; set; } = "Asia/Bangkok";

	public ICollection<User> Users { get; set; } = [];
	public ICollection<Category> Categories { get; set; } = [];
	public ICollection<Product> Products { get; set; } = [];
	public ICollection<Order> Orders { get; set; } = [];
}

public sealed class User : ITenantEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public string Email { get; set; } = string.Empty;
	public string PasswordHash { get; set; } = string.Empty;
	public string FullName { get; set; } = string.Empty;
	public string EmployeeCode { get; set; } = string.Empty;
	public string PositionTitle { get; set; } = string.Empty;
	public UserRole Role { get; set; } = UserRole.Cashier;
	public bool CanProcessCheckout { get; set; } = true;
	public bool CanManageProducts { get; set; } = true;
	public bool CanViewReports { get; set; } = true;
	public bool CanManageUsers { get; set; } = true;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	public Tenant? Tenant { get; set; }
}

public sealed class AuditLog : ITenantEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public string Action { get; set; } = string.Empty;
	public string Category { get; set; } = string.Empty;
	public string PerformedBy { get; set; } = string.Empty;
	public string Details { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	public Tenant? Tenant { get; set; }
}

public sealed class Category : ITenantEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Icon { get; set; } = "tag";

	public Tenant? Tenant { get; set; }
	public ICollection<Product> Products { get; set; } = [];
}

public sealed class Product : ITenantEntity, IVersionedEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public int CategoryId { get; set; }
	public string Barcode { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public decimal Price { get; set; }
	public decimal Cost { get; set; }
	public int StockQuantity { get; set; }
	public int MinimumStock { get; set; } = 5;
	public string Unit { get; set; } = "pcs";
	public string? ImageUrl { get; set; }
	public bool IsActive { get; set; } = true;
	public long Version { get; set; }

	public Tenant? Tenant { get; set; }
	public Category? Category { get; set; }
}

public sealed class Order : ITenantEntity, IVersionedEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public string OrderNo { get; set; } = string.Empty;
	public decimal SubTotalAmount { get; set; }
	public decimal ServiceChargeAmount { get; set; }
	public decimal VatAmount { get; set; }
	public decimal TotalAmount { get; set; }
	public decimal DiscountAmount { get; set; }
	public decimal ManualDiscountAmount { get; set; }
	public decimal CouponDiscountAmount { get; set; }
	public decimal LoyaltyDiscountAmount { get; set; }
	public decimal PaidAmount { get; set; }
	public decimal ChangeAmount { get; set; }
	public decimal TotalRefundedAmount { get; set; }
	public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
	public OrderStatus Status { get; set; } = OrderStatus.Completed;
	public string CashierName { get; set; } = string.Empty;
	public int? CustomerId { get; set; }
	public string? CouponCode { get; set; }
	public int LoyaltyPointsEarned { get; set; }
	public int LoyaltyPointsRedeemed { get; set; }
	public int? CashShiftId { get; set; }
	public string? IdempotencyKey { get; set; }
	public string? RequestFingerprint { get; set; }
	public long Version { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	public Tenant? Tenant { get; set; }
	public Customer? Customer { get; set; }
	public CashShift? CashShift { get; set; }
	public ICollection<OrderItem> Items { get; set; } = [];
	public ICollection<OrderReversal> Reversals { get; set; } = [];
}

public sealed class OrderItem : ITenantEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public int OrderId { get; set; }
	public int ProductId { get; set; }
	public string ProductName { get; set; } = string.Empty;
	public string Barcode { get; set; } = string.Empty;
	public decimal UnitPrice { get; set; }
	public int Quantity { get; set; }
	public decimal SubTotal { get; set; }
	public int RefundedQuantity { get; set; }

	public Order? Order { get; set; }
	public Product? Product { get; set; }
}

public sealed class StockTransaction : ITenantEntity
{
	public int Id { get; set; }
	public int TenantId { get; set; }
	public int ProductId { get; set; }
	public int QuantityChange { get; set; }
	public StockTransactionType Type { get; set; }
	public string Note { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	public Tenant? Tenant { get; set; }
	public Product? Product { get; set; }
}
