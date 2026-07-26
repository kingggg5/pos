using System.ComponentModel.DataAnnotations;
using SmartPos.Api.Models;

namespace SmartPos.Api.Dtos;

public record CreateOrderQuoteDto(
	[Required, MinLength(1)] List<CreateOrderItemDto> Items,
	[Range(0, 1_000_000)] decimal DiscountAmount = 0,
	[MaxLength(30)] string? CustomerPhone = null,
	[MaxLength(50)] string? CouponCode = null,
	[Range(0, 1_000_000)] int LoyaltyPointsToRedeem = 0);

public record OrderQuoteDto(
	decimal SubTotalAmount,
	decimal ManualDiscountAmount,
	decimal CouponDiscountAmount,
	decimal LoyaltyDiscountAmount,
	decimal TotalDiscountAmount,
	decimal ServiceChargeAmount,
	decimal VatAmount,
	decimal TotalAmount,
	int? CustomerId,
	string? CustomerName,
	int CustomerPointsBalance,
	int LoyaltyPointsRedeemed,
	int LoyaltyPointsEarned,
	string? CouponCode);

public record OpenCashShiftRequest(
	[Range(0, 10_000_000)] decimal OpeningCash,
	[MaxLength(500)] string? OpeningNote,
	[Required, MaxLength(100)] string IdempotencyKey);

public record CloseCashShiftRequest(
	[Range(0, 10_000_000)] decimal ClosingCash,
	[MaxLength(500)] string? ClosingNote,
	[Required, MaxLength(100)] string IdempotencyKey);

public record CashShiftDto(
	int Id,
	CashShiftStatus Status,
	decimal OpeningCash,
	decimal CashSalesAmount,
	decimal CashRefundAmount,
	decimal ExpectedCash,
	decimal? ClosingCash,
	decimal? Difference,
	DateTime OpenedAt,
	DateTime? ClosedAt,
	int OpenedByUserId,
	string OpenedByName,
	int? ClosedByUserId,
	string? ClosedByName,
	string OpeningNote,
	string ClosingNote,
	long Version);

public record ReverseOrderRequest(
	[Required, MinLength(3), MaxLength(500)] string Reason,
	[Required, MaxLength(100)] string IdempotencyKey);

public record PartialRefundOrderItemRequest(
	[Range(1, int.MaxValue)] int OrderItemId,
	[Range(1, 10_000)] int Quantity);

public record PartialRefundOrderRequest(
	[Required, MinLength(1)] List<PartialRefundOrderItemRequest> Items,
	[Required, MinLength(3), MaxLength(500)] string Reason,
	[Required, MaxLength(100)] string IdempotencyKey);

public record OrderReversalItemDto(
	int OrderItemId,
	int ProductId,
	string ProductName,
	int Quantity,
	decimal SubTotalAmount,
	decimal ManualDiscountAmount,
	decimal CouponDiscountAmount,
	decimal LoyaltyDiscountAmount,
	decimal ServiceChargeAmount,
	decimal VatAmount,
	decimal TotalAmount);

public record OrderReversalDto(
	int Id,
	int OrderId,
	string OrderNo,
	OrderReversalType Type,
	decimal Amount,
	bool StockRestored,
	string Reason,
	string IdempotencyKey,
	string PerformedBy,
	DateTime ProcessedAt,
	decimal SubTotalAmount,
	decimal ManualDiscountAmount,
	decimal CouponDiscountAmount,
	decimal LoyaltyDiscountAmount,
	decimal ServiceChargeAmount,
	decimal VatAmount,
	bool IsFullOrderReversal,
	int LoyaltyPointsEarnedReversed,
	int LoyaltyPointsRedeemedRestored,
	bool CouponUsageReleased,
	List<OrderReversalItemDto> Items);

public record SaveCustomerRequest(
	[Required, MaxLength(30)] string Phone,
	[Required, MaxLength(160)] string Name,
	[EmailAddress, MaxLength(254)] string? Email);

public record CustomerDto(
	int Id,
	string Phone,
	string Name,
	string? Email,
	int PointsBalance,
	int LifetimePointsEarned,
	int LifetimePointsRedeemed,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	long Version);

public record LoyaltyTransactionDto(
	int Id,
	LoyaltyTransactionType Type,
	int PointsChange,
	int BalanceAfter,
	int? OrderId,
	string? OrderNo,
	string Description,
	DateTime CreatedAt);

public record SavePromotionRequest(
	[Required, MaxLength(50)] string Code,
	[Required, MaxLength(160)] string Name,
	[MaxLength(500)] string? Description,
	[EnumDataType(typeof(PromotionDiscountType))] PromotionDiscountType DiscountType,
	[Range(0.01, 1_000_000)] decimal Value,
	[Range(0, 1_000_000)] decimal MinimumOrderAmount = 0,
	[Range(0.01, 1_000_000)] decimal? MaximumDiscountAmount = null,
	[Range(1, 10_000_000)] int? UsageLimit = null,
	DateTime? ValidFrom = null,
	DateTime? ValidUntil = null,
	bool IsActive = true);

public record PromotionDto(
	int Id,
	string Code,
	string Name,
	string Description,
	PromotionDiscountType DiscountType,
	decimal Value,
	decimal MinimumOrderAmount,
	decimal? MaximumDiscountAmount,
	int? UsageLimit,
	int UsageCount,
	DateTime? ValidFrom,
	DateTime? ValidUntil,
	bool IsActive,
	long Version);

public record ValidatePromotionRequest(
	[Required, MaxLength(50)] string Code,
	[Range(0, 1_000_000)] decimal SubTotal);

public record CouponValidationDto(
	bool IsValid,
	string Code,
	PromotionDiscountType? DiscountType,
	decimal? Value,
	decimal DiscountAmount,
	string Message,
	decimal MinimumOrderAmount,
	decimal? MaximumDiscountAmount,
	DateTime? ValidFrom,
	DateTime? ValidUntil);
