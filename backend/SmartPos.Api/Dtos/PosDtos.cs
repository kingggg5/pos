using System.ComponentModel.DataAnnotations;
using SmartPos.Api.Models;

namespace SmartPos.Api.Dtos;

public record RegisterStoreDto(
	[Required, MaxLength(100)] string StoreName,
	[Required, MaxLength(50)] string StoreSlug,
	[Required, EmailAddress] string OwnerEmail,
	[Required, MinLength(6)] string OwnerPassword,
	[Required, MaxLength(100)] string OwnerFullName);

public record UserDto(
	int Id,
	string Email,
	string FullName,
	string EmployeeCode,
	string PositionTitle,
	UserRole Role,
	bool CanProcessCheckout,
	bool CanManageProducts,
	bool CanViewReports,
	bool CanManageUsers,
	DateTime CreatedAt
);

public record AuthResponseDto(
	int UserId,
	string Email,
	string FullName,
	UserRole Role,
	int TenantId,
	string TenantName,
	string TenantSlug,
	string Token
);

public record CreateStaffRequest(
	[Required, EmailAddress, MaxLength(254)] string Email,
	[Required, MinLength(8), MaxLength(128)] string Password,
	[Required, MaxLength(160)] string FullName,
	[MaxLength(50)] string EmployeeCode,
	[MaxLength(100)] string PositionTitle,
	[EnumDataType(typeof(UserRole))]
	UserRole Role,
	bool CanProcessCheckout = true,
	bool CanManageProducts = true,
	bool CanViewReports = true,
	bool CanManageUsers = false
);

public record UpdateStaffRequest(
	[Required, MaxLength(160)] string FullName,
	[Required, MaxLength(50)] string EmployeeCode,
	[Required, MaxLength(100)] string PositionTitle,
	[EnumDataType(typeof(UserRole))]
	UserRole Role,
	bool CanProcessCheckout,
	bool CanManageProducts,
	bool CanViewReports,
	bool CanManageUsers
);

public record AuditLogDto(
	int Id,
	string Action,
	string Category,
	string PerformedBy,
	string Details,
	DateTime CreatedAt
);

public record StoreSummaryDto(
	int TenantId,
	string Name,
	string Slug,
	string Plan,
	int UsersCount,
	int ProductsCount,
	int OrdersCount,
	decimal TotalRevenue,
	DateTime CreatedAt
);

public record CategoryDistributionDto(
	string CategoryName,
	int ProductsCount,
	int TotalSoldQuantity
);

public record PlatformDashboardDto(
	int TotalStoresCount,
	decimal TotalPlatformRevenue,
	int TotalProductsCount,
	int TotalOrdersCount,
	int TotalUsersCount,
	int TotalVisitsCount,
	List<StoreSummaryDto> Stores,
	List<CategoryDistributionDto> Categories,
	List<AuditLogDto> RecentLogs
);

public record LoginDto(
	[Required, EmailAddress] string Email,
	[Required] string Password);

public record TenantDto(
	int Id,
	string Name,
	string Slug,
	string Plan,
	bool IsActive,
	DateTime CreatedAt);

public record StoreSettingsDto(
	int Id,
	string Name,
	string Slug,
	string? QrCodeUrl,
	decimal VatRate,
	decimal ServiceChargeRate,
	string ReceiptHeaderNote,
	string ReceiptFooterNote,
	string BusinessTimeZoneId
);

public record UpdateStoreSettingsRequest(
	[Url, MaxLength(2_000)]
	string? QrCodeUrl,
	[Range(0, 100)]
	decimal VatRate,
	[Range(0, 100)]
	decimal ServiceChargeRate,
	[Required, MaxLength(500)]
	string ReceiptHeaderNote,
	[Required, MaxLength(500)]
	string ReceiptFooterNote,
	[MaxLength(100)]
	string? BusinessTimeZoneId = null
);

public record CategoryDto(
	int Id,
	string Name,
	string Icon);

public record ProductDto(
	int Id,
	int CategoryId,
	string CategoryName,
	string Barcode,
	string Name,
	decimal Price,
	decimal Cost,
	int StockQuantity,
	int MinimumStock,
	string Unit,
	string? ImageUrl,
	bool IsActive,
	bool IsLowStock);

public record SaveProductDto(
	int Id = 0,
	[Range(1, int.MaxValue)] int CategoryId = 1,
	[Required, MaxLength(50)] string Barcode = "",
	[Required, MaxLength(160)] string Name = "",
	[Range(0, 1_000_000)] decimal Price = 0,
	[Range(0, 1_000_000)] decimal Cost = 0,
	[Range(0, 100_000)] int StockQuantity = 0,
	[Range(0, 100_000)] int MinimumStock = 5,
	[Required, MaxLength(30)] string Unit = "pcs",
	string? ImageUrl = null);

public record CreateOrderItemDto(
	[Range(1, int.MaxValue)] int ProductId,
	[Range(1, 10_000)] int Quantity);

public record CreateOrderDto(
	[Required, MinLength(1)] List<CreateOrderItemDto> Items,
	[Range(0, 1_000_000)] decimal DiscountAmount,
	[Range(0, 1_000_000)] decimal PaidAmount,
	[EnumDataType(typeof(PaymentMethod))] PaymentMethod PaymentMethod,
	[Required, MaxLength(100)] string IdempotencyKey,
	[MaxLength(30)] string? CustomerPhone = null,
	[MaxLength(50)] string? CouponCode = null,
	[Range(0, 1_000_000)] int LoyaltyPointsToRedeem = 0);

public record OrderItemDto(
	int Id,
	int ProductId,
	string ProductName,
	string Barcode,
	decimal UnitPrice,
	int Quantity,
	decimal SubTotal,
	int RefundedQuantity,
	int RefundableQuantity);

public record OrderDto(
	int Id,
	string OrderNo,
	decimal SubTotalAmount,
	decimal ServiceChargeAmount,
	decimal VatAmount,
	decimal TotalAmount,
	decimal DiscountAmount,
	decimal ManualDiscountAmount,
	decimal CouponDiscountAmount,
	decimal LoyaltyDiscountAmount,
	decimal PaidAmount,
	decimal ChangeAmount,
	PaymentMethod PaymentMethod,
	OrderStatus Status,
	string CashierName,
	int? CustomerId,
	string? CustomerName,
	string? CustomerPhone,
	string? CouponCode,
	int LoyaltyPointsEarned,
	int LoyaltyPointsRedeemed,
	int? CashShiftId,
	long Version,
	DateTime CreatedAt,
	List<OrderItemDto> Items,
	decimal TotalRefundedAmount);

public record ZReportSummaryDto(
	decimal TodayTotalRevenue,
	int TodayTotalOrders,
	int TotalProductsCount,
	int LowStockProductsCount,
	double AverageOrderValue,
	List<ProductDto> TopSellingProducts,
	string BusinessTimeZoneId,
	DateOnly BusinessDate,
	DateTime BusinessDayStartUtc,
	DateTime BusinessDayEndUtc,
	decimal TodayGrossSales,
	decimal TodayRefundAmount,
	decimal TodayVoidAmount,
	int TodayReversalEvents);
