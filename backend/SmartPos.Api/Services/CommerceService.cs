using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Dtos;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;

namespace SmartPos.Api.Services;

public sealed class PosBusinessException(
	string code,
	string message,
	int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
{
	public string Code { get; } = code;
	public int StatusCode { get; } = statusCode;
}

public sealed record PosActor(int UserId, string DisplayName, UserRole Role);

public interface ICommerceService
{
	Task<OrderQuoteDto> QuoteAsync(CreateOrderQuoteDto request, CancellationToken cancellationToken);
	Task<OrderDto> CheckoutAsync(CreateOrderDto request, PosActor actor, CancellationToken cancellationToken);
	Task<OrderReversalDto> ReverseOrderAsync(int orderId, OrderReversalType type, ReverseOrderRequest request, PosActor actor, CancellationToken cancellationToken);
	Task<OrderReversalDto> RefundItemsAsync(int orderId, PartialRefundOrderRequest request, PosActor actor, CancellationToken cancellationToken);
}

public sealed class CommerceService(
	SmartPosDbContext dbContext,
	ITenantProvider tenantProvider) : ICommerceService
{
	private const decimal BahtPerPointEarned = 10m;
	private const decimal BahtPerPointRedeemed = 1m;

	public async Task<OrderQuoteDto> QuoteAsync(CreateOrderQuoteDto request, CancellationToken cancellationToken)
	{
		var pricing = await CalculatePricingAsync(
			request.Items,
			request.DiscountAmount,
			request.CustomerPhone,
			request.CouponCode,
			request.LoyaltyPointsToRedeem,
			tracking: false,
			cancellationToken);

		return ToQuoteDto(pricing);
	}

	public async Task<OrderDto> CheckoutAsync(
		CreateOrderDto request,
		PosActor actor,
		CancellationToken cancellationToken)
	{
		var tenantId = RequireTenantId();
		var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
		var requestFingerprint = CreateCheckoutFingerprint(request);

		var priorOrder = await dbContext.Orders
			.AsNoTracking()
			.Include(order => order.Customer)
			.Include(order => order.Items)
			.FirstOrDefaultAsync(order => order.IdempotencyKey == idempotencyKey, cancellationToken);

		if (priorOrder is not null)
		{
			if (!string.Equals(priorOrder.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
				throw new PosBusinessException("IDEMPOTENCY_KEY_REUSED", "The idempotency key was already used for a different checkout.", StatusCodes.Status409Conflict);

			return ToOrderDto(priorOrder);
		}

		var strategy = dbContext.Database.CreateExecutionStrategy();
		return await strategy.ExecuteAsync(async () =>
		{
			await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
			try
			{
				var duplicate = await dbContext.Orders
					.Include(order => order.Customer)
					.Include(order => order.Items)
					.FirstOrDefaultAsync(order => order.IdempotencyKey == idempotencyKey, cancellationToken);
				if (duplicate is not null)
				{
					if (!string.Equals(duplicate.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
						throw new PosBusinessException("IDEMPOTENCY_KEY_REUSED", "The idempotency key was already used for a different checkout.", StatusCodes.Status409Conflict);

					await transaction.CommitAsync(cancellationToken);
					return ToOrderDto(duplicate);
				}

				var pricing = await CalculatePricingAsync(
					request.Items,
					request.DiscountAmount,
					request.CustomerPhone,
					request.CouponCode,
					request.LoyaltyPointsToRedeem,
					tracking: true,
					cancellationToken);

				if (request.PaidAmount < pricing.TotalAmount)
					throw new PosBusinessException("PAYMENT_INSUFFICIENT", $"Paid amount must be at least {pricing.TotalAmount:N2}.");
				if (request.PaymentMethod != PaymentMethod.Cash && request.PaidAmount != pricing.TotalAmount)
					throw new PosBusinessException("PAYMENT_AMOUNT_INVALID", "Non-cash payment must exactly match the order total.");

				CashShift? cashShift = null;
				if (request.PaymentMethod == PaymentMethod.Cash)
				{
					cashShift = await dbContext.CashShifts
						.SingleOrDefaultAsync(shift => shift.Status == CashShiftStatus.Open, cancellationToken);
					if (cashShift is null)
						throw new PosBusinessException("CASH_SHIFT_REQUIRED", "Open a cash shift before accepting a cash payment.", StatusCodes.Status409Conflict);
				}

				var now = DateTime.UtcNow;
				var order = new Order
				{
					TenantId = tenantId,
					OrderNo = $"POS-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30],
					SubTotalAmount = pricing.SubTotalAmount,
					ServiceChargeAmount = pricing.ServiceChargeAmount,
					VatAmount = pricing.VatAmount,
					TotalAmount = pricing.TotalAmount,
					DiscountAmount = pricing.TotalDiscountAmount,
					ManualDiscountAmount = pricing.ManualDiscountAmount,
					CouponDiscountAmount = pricing.CouponDiscountAmount,
					LoyaltyDiscountAmount = pricing.LoyaltyDiscountAmount,
					PaidAmount = request.PaidAmount,
					ChangeAmount = RoundMoney(request.PaidAmount - pricing.TotalAmount),
					PaymentMethod = request.PaymentMethod,
					Status = OrderStatus.Completed,
					CashierName = actor.DisplayName,
					CustomerId = pricing.Customer?.Id,
					CouponCode = pricing.Promotion?.Code,
					LoyaltyPointsEarned = pricing.LoyaltyPointsEarned,
					LoyaltyPointsRedeemed = pricing.LoyaltyPointsRedeemed,
					CashShiftId = cashShift?.Id,
					IdempotencyKey = idempotencyKey,
					RequestFingerprint = requestFingerprint,
					CreatedAt = now
				};

				foreach (var line in pricing.Lines)
				{
					line.Product.StockQuantity -= line.Quantity;
					order.Items.Add(new OrderItem
					{
						TenantId = tenantId,
						ProductId = line.Product.Id,
						ProductName = line.Product.Name,
						Barcode = line.Product.Barcode,
						UnitPrice = line.Product.Price,
						Quantity = line.Quantity,
						SubTotal = line.SubTotal
					});
					dbContext.StockTransactions.Add(new StockTransaction
					{
						TenantId = tenantId,
						ProductId = line.Product.Id,
						QuantityChange = -line.Quantity,
						Type = StockTransactionType.Sale,
						Note = $"POS sale {order.OrderNo}"
					});
				}

				dbContext.Orders.Add(order);
				dbContext.FinancialEvents.Add(new FinancialEvent
				{
					TenantId = tenantId,
					Type = FinancialEventType.Sale,
					Order = order,
					SourceKey = $"SALE:{order.OrderNo}",
					Amount = order.TotalAmount,
					PaymentMethod = order.PaymentMethod,
					CashShift = cashShift,
					OccurredAt = now,
					Description = $"Sale {order.OrderNo}"
				});
				if (cashShift is not null)
				{
					cashShift.CashSalesAmount = RoundMoney(cashShift.CashSalesAmount + order.TotalAmount);
					cashShift.ExpectedCash = RoundMoney(cashShift.ExpectedCash + order.TotalAmount);
				}

				if (pricing.Customer is not null)
				{
					if (pricing.LoyaltyPointsRedeemed > 0)
					{
						pricing.Customer.PointsBalance -= pricing.LoyaltyPointsRedeemed;
						pricing.Customer.LifetimePointsRedeemed += pricing.LoyaltyPointsRedeemed;
						dbContext.LoyaltyTransactions.Add(new LoyaltyTransaction
						{
							TenantId = tenantId,
							Customer = pricing.Customer,
							Order = order,
							Type = LoyaltyTransactionType.Redeem,
							PointsChange = -pricing.LoyaltyPointsRedeemed,
							BalanceAfter = pricing.Customer.PointsBalance,
							Description = $"Redeemed on {order.OrderNo}"
						});
					}

					if (pricing.LoyaltyPointsEarned > 0)
					{
						pricing.Customer.PointsBalance += pricing.LoyaltyPointsEarned;
						pricing.Customer.LifetimePointsEarned += pricing.LoyaltyPointsEarned;
						dbContext.LoyaltyTransactions.Add(new LoyaltyTransaction
						{
							TenantId = tenantId,
							Customer = pricing.Customer,
							Order = order,
							Type = LoyaltyTransactionType.Earn,
							PointsChange = pricing.LoyaltyPointsEarned,
							BalanceAfter = pricing.Customer.PointsBalance,
							Description = $"Earned from {order.OrderNo}"
						});
					}

					pricing.Customer.UpdatedAt = now;
				}

				if (pricing.Promotion is not null)
				{
					pricing.Promotion.UsageCount++;
					pricing.Promotion.UpdatedAt = now;
					dbContext.CouponRedemptions.Add(new CouponRedemption
					{
						TenantId = tenantId,
						PromotionCoupon = pricing.Promotion,
						Order = order,
						Customer = pricing.Customer,
						DiscountAmount = pricing.CouponDiscountAmount,
						RedeemedAt = now
					});
				}

				dbContext.AuditLogs.Add(new AuditLog
				{
					TenantId = tenantId,
					Action = "ORDER_CHECKOUT",
					Category = "Order",
					PerformedBy = actor.DisplayName,
					Details = $"Completed {order.OrderNo} for THB {order.TotalAmount:N2} ({order.PaymentMethod})",
					CreatedAt = now
				});

				await dbContext.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				return ToOrderDto(order);
			}
			catch (DbUpdateConcurrencyException)
			{
				await transaction.RollbackAsync(cancellationToken);
				throw new PosBusinessException("CONCURRENT_UPDATE", "Stock, points, or promotion usage changed. Please review and retry.", StatusCodes.Status409Conflict);
			}
			catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
			{
				await transaction.RollbackAsync(cancellationToken);
				throw new PosBusinessException("DUPLICATE_OPERATION", "This checkout was already processed or conflicts with another operation.", StatusCodes.Status409Conflict);
			}
		});
	}

	public async Task<OrderReversalDto> ReverseOrderAsync(
		int orderId,
		OrderReversalType type,
		ReverseOrderRequest request,
		PosActor actor,
		CancellationToken cancellationToken)
	{
		if (type == OrderReversalType.PartialRefund)
			throw new PosBusinessException("REVERSAL_TYPE_INVALID", "Use the partial refund endpoint to refund selected items.");

		return await ProcessReversalAsync(
			orderId,
			type,
			request.Reason,
			request.IdempotencyKey,
			requestedItems: null,
			actor,
			cancellationToken);
	}

	public Task<OrderReversalDto> RefundItemsAsync(
		int orderId,
		PartialRefundOrderRequest request,
		PosActor actor,
		CancellationToken cancellationToken) =>
		ProcessReversalAsync(
			orderId,
			OrderReversalType.PartialRefund,
			request.Reason,
			request.IdempotencyKey,
			request.Items,
			actor,
			cancellationToken);

	private async Task<OrderReversalDto> ProcessReversalAsync(
		int orderId,
		OrderReversalType type,
		string reasonValue,
		string idempotencyKeyValue,
		IReadOnlyCollection<PartialRefundOrderItemRequest>? requestedItems,
		PosActor actor,
		CancellationToken cancellationToken)
	{
		var tenantId = RequireTenantId();
		var reason = NormalizeReversalReason(reasonValue);
		var idempotencyKey = NormalizeIdempotencyKey(idempotencyKeyValue);
		ValidatePartialRefundRequest(type, requestedItems);
		var requestFingerprint = CreateReversalFingerprint(orderId, type, reason, requestedItems);

		var existing = await dbContext.OrderReversals
			.AsNoTracking()
			.Include(reversal => reversal.Order)
			.Include(reversal => reversal.Items)
			.FirstOrDefaultAsync(reversal => reversal.IdempotencyKey == idempotencyKey, cancellationToken);
		if (existing is not null)
			return ValidateIdempotentReversal(existing, orderId, type, requestFingerprint);

		var strategy = dbContext.Database.CreateExecutionStrategy();
		return await strategy.ExecuteAsync(async () =>
		{
			await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
			try
			{
				var duplicate = await dbContext.OrderReversals
					.Include(reversal => reversal.Order)
					.Include(reversal => reversal.Items)
					.FirstOrDefaultAsync(reversal => reversal.IdempotencyKey == idempotencyKey, cancellationToken);
				if (duplicate is not null)
				{
					await transaction.CommitAsync(cancellationToken);
					return ValidateIdempotentReversal(duplicate, orderId, type, requestFingerprint);
				}

				var order = await dbContext.Orders
					.Include(value => value.Items)
					.Include(value => value.Customer)
					.Include(value => value.Reversals)
					.SingleOrDefaultAsync(value => value.Id == orderId, cancellationToken)
					?? throw new PosBusinessException("ORDER_NOT_FOUND", "Order was not found.", StatusCodes.Status404NotFound);

				ValidateOrderCanBeReversed(order, type);
				var refundRequests = ResolveRefundLines(order, type, requestedItems);

				CashShift? cashShift = null;
				if (order.PaymentMethod == PaymentMethod.Cash)
				{
					cashShift = await dbContext.CashShifts
						.SingleOrDefaultAsync(shift => shift.Status == CashShiftStatus.Open, cancellationToken);
					if (cashShift is null)
						throw new PosBusinessException("CASH_SHIFT_REQUIRED", "Open a cash shift before reversing a cash payment.", StatusCodes.Status409Conflict);
				}

				var productIds = refundRequests.Select(item => item.OrderItem.ProductId).Distinct().ToList();
				var products = await dbContext.Products
					.Where(product => productIds.Contains(product.Id))
					.ToDictionaryAsync(product => product.Id, cancellationToken);
				if (products.Count != productIds.Count)
					throw new PosBusinessException("PRODUCT_MISSING", "One or more products from this order no longer exist.", StatusCodes.Status409Conflict);

				var lineAllocations = CreateLineAllocations(order);
				var now = DateTime.UtcNow;
				var reversal = new OrderReversal
				{
					TenantId = tenantId,
					Order = order,
					Type = type,
					StockRestored = true,
					Reason = reason,
					IdempotencyKey = idempotencyKey,
					RequestFingerprint = requestFingerprint,
					PerformedByUserId = actor.UserId,
					PerformedBy = actor.DisplayName,
					CashShift = cashShift,
					ProcessedAt = now
				};

				foreach (var refundRequest in refundRequests.OrderBy(value => value.OrderItem.Id))
				{
					var item = refundRequest.OrderItem;
					var allocation = lineAllocations[item.Id];
					var reversalItem = new OrderReversalItem
					{
						TenantId = tenantId,
						OrderItem = item,
						ProductId = item.ProductId,
						ProductName = item.ProductName,
						Quantity = refundRequest.Quantity,
						SubTotalAmount = AllocateRefundChunk(allocation.SubTotalAmount, item.Quantity, item.RefundedQuantity, refundRequest.Quantity),
						ManualDiscountAmount = AllocateRefundChunk(allocation.ManualDiscountAmount, item.Quantity, item.RefundedQuantity, refundRequest.Quantity),
						CouponDiscountAmount = AllocateRefundChunk(allocation.CouponDiscountAmount, item.Quantity, item.RefundedQuantity, refundRequest.Quantity),
						LoyaltyDiscountAmount = AllocateRefundChunk(allocation.LoyaltyDiscountAmount, item.Quantity, item.RefundedQuantity, refundRequest.Quantity),
						ServiceChargeAmount = AllocateRefundChunk(allocation.ServiceChargeAmount, item.Quantity, item.RefundedQuantity, refundRequest.Quantity),
						VatAmount = AllocateRefundChunk(allocation.VatAmount, item.Quantity, item.RefundedQuantity, refundRequest.Quantity)
					};
					reversalItem.TotalAmount = RoundMoney(
						reversalItem.SubTotalAmount -
						reversalItem.ManualDiscountAmount -
						reversalItem.CouponDiscountAmount -
						reversalItem.LoyaltyDiscountAmount +
						reversalItem.ServiceChargeAmount +
						reversalItem.VatAmount);
					reversal.Items.Add(reversalItem);

					item.RefundedQuantity += refundRequest.Quantity;
					var product = products[item.ProductId];
					product.StockQuantity += refundRequest.Quantity;
					dbContext.StockTransactions.Add(new StockTransaction
					{
						TenantId = tenantId,
						ProductId = product.Id,
						QuantityChange = refundRequest.Quantity,
						Type = type == OrderReversalType.Void ? StockTransactionType.VoidRestock : StockTransactionType.RefundRestock,
						Note = $"{type} {order.OrderNo}: {reason}",
						CreatedAt = now
					});
				}

				reversal.SubTotalAmount = RoundMoney(reversal.Items.Sum(item => item.SubTotalAmount));
				reversal.ManualDiscountAmount = RoundMoney(reversal.Items.Sum(item => item.ManualDiscountAmount));
				reversal.CouponDiscountAmount = RoundMoney(reversal.Items.Sum(item => item.CouponDiscountAmount));
				reversal.LoyaltyDiscountAmount = RoundMoney(reversal.Items.Sum(item => item.LoyaltyDiscountAmount));
				reversal.ServiceChargeAmount = RoundMoney(reversal.Items.Sum(item => item.ServiceChargeAmount));
				reversal.VatAmount = RoundMoney(reversal.Items.Sum(item => item.VatAmount));
				reversal.Amount = RoundMoney(reversal.Items.Sum(item => item.TotalAmount));

				var allItemsReversed = order.Items.All(item => item.RefundedQuantity == item.Quantity);
				reversal.IsFullOrderReversal = allItemsReversed;
				order.TotalRefundedAmount = allItemsReversed
					? order.TotalAmount
					: RoundMoney(order.TotalRefundedAmount + reversal.Amount);
				order.Status = type == OrderReversalType.Void
					? OrderStatus.Cancelled
					: allItemsReversed ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;

				ApplyLoyaltyReversal(order, reversal, allItemsReversed, tenantId, now);

				var couponRedemption = await dbContext.CouponRedemptions
					.Include(value => value.PromotionCoupon)
					.SingleOrDefaultAsync(value => value.OrderId == order.Id, cancellationToken);
				if (allItemsReversed &&
					couponRedemption is { ReversedAt: null, PromotionCoupon: { } coupon })
				{
					coupon.UsageCount = Math.Max(0, coupon.UsageCount - 1);
					coupon.UpdatedAt = now;
					couponRedemption.ReversedAt = now;
					reversal.CouponUsageReleased = true;
				}

				dbContext.OrderReversals.Add(reversal);
				dbContext.FinancialEvents.Add(new FinancialEvent
				{
					TenantId = tenantId,
					Type = type == OrderReversalType.Void ? FinancialEventType.Void : FinancialEventType.Refund,
					Order = order,
					OrderReversal = reversal,
					SourceKey = $"REVERSAL:{idempotencyKey}",
					Amount = -reversal.Amount,
					PaymentMethod = order.PaymentMethod,
					CashShift = cashShift,
					OccurredAt = now,
					Description = $"{type} {order.OrderNo}"
				});
				if (cashShift is not null)
				{
					cashShift.CashRefundAmount = RoundMoney(cashShift.CashRefundAmount + reversal.Amount);
					cashShift.ExpectedCash = RoundMoney(cashShift.ExpectedCash - reversal.Amount);
				}
				dbContext.AuditLogs.Add(new AuditLog
				{
					TenantId = tenantId,
					Action = type == OrderReversalType.Void
						? "ORDER_VOIDED"
						: type == OrderReversalType.PartialRefund ? "ORDER_PARTIALLY_REFUNDED" : "ORDER_REFUNDED",
					Category = "Order",
					PerformedBy = actor.DisplayName,
					Details = $"{type} {order.OrderNo}, THB {reversal.Amount:N2}: {reason}",
					CreatedAt = now
				});

				await dbContext.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				return ToReversalDto(reversal);
			}
			catch (DbUpdateConcurrencyException)
			{
				await transaction.RollbackAsync(cancellationToken);
				throw new PosBusinessException("CONCURRENT_UPDATE", "This order, its stock, points, coupon, or shift changed. Please retry.", StatusCodes.Status409Conflict);
			}
			catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
			{
				await transaction.RollbackAsync(cancellationToken);
				throw new PosBusinessException("DUPLICATE_OPERATION", "This reversal was already processed or conflicts with another operation.", StatusCodes.Status409Conflict);
			}
		});
	}

	private async Task<CheckoutPricing> CalculatePricingAsync(
		IReadOnlyCollection<CreateOrderItemDto> requestedItems,
		decimal manualDiscount,
		string? customerPhone,
		string? couponCode,
		int pointsToRedeem,
		bool tracking,
		CancellationToken cancellationToken)
	{
		if (requestedItems.Count == 0)
			throw new PosBusinessException("CART_EMPTY", "Cart cannot be empty.");

		var groupedItems = requestedItems
			.GroupBy(item => item.ProductId)
			.Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
			.ToList();
		if (groupedItems.Any(item => item.Quantity <= 0 || item.Quantity > 10_000))
			throw new PosBusinessException("INVALID_QUANTITY", "Item quantity must be between 1 and 10,000.");

		var productIds = groupedItems.Select(item => item.ProductId).ToList();
		IQueryable<Product> productQuery = dbContext.Products.Where(product => product.IsActive && productIds.Contains(product.Id));
		if (!tracking)
			productQuery = productQuery.AsNoTracking();
		var products = await productQuery.ToDictionaryAsync(product => product.Id, cancellationToken);
		if (products.Count != productIds.Count)
			throw new PosBusinessException("PRODUCT_NOT_FOUND", "One or more products were not found or inactive.");

		var lines = new List<CheckoutLine>(groupedItems.Count);
		foreach (var requested in groupedItems)
		{
			var product = products[requested.ProductId];
			if (product.StockQuantity < requested.Quantity)
				throw new PosBusinessException("INSUFFICIENT_STOCK", $"Insufficient stock for {product.Name}. Available: {product.StockQuantity}", StatusCodes.Status409Conflict);
			lines.Add(new CheckoutLine(product, requested.Quantity, RoundMoney(product.Price * requested.Quantity)));
		}

		var subTotal = RoundMoney(lines.Sum(line => line.SubTotal));
		var roundedManualDiscount = RoundMoney(manualDiscount);
		if (roundedManualDiscount > subTotal)
			throw new PosBusinessException("DISCOUNT_EXCEEDS_SUBTOTAL", "Manual discount cannot exceed the cart subtotal.");

		Customer? customer = null;
		if (!string.IsNullOrWhiteSpace(customerPhone))
		{
			var normalizedPhone = NormalizePhone(customerPhone);
			IQueryable<Customer> customerQuery = dbContext.Customers.Where(value => value.PhoneNormalized == normalizedPhone);
			if (!tracking)
				customerQuery = customerQuery.AsNoTracking();
			customer = await customerQuery.SingleOrDefaultAsync(cancellationToken)
				?? throw new PosBusinessException("CUSTOMER_NOT_FOUND", "No member was found for this phone number.", StatusCodes.Status404NotFound);
		}

		PromotionCoupon? promotion = null;
		decimal couponDiscount = 0;
		if (!string.IsNullOrWhiteSpace(couponCode))
		{
			var normalizedCode = couponCode.Trim().ToUpperInvariant();
			IQueryable<PromotionCoupon> promotionQuery = dbContext.PromotionCoupons.Where(value => value.Code == normalizedCode);
			if (!tracking)
				promotionQuery = promotionQuery.AsNoTracking();
			promotion = await promotionQuery.SingleOrDefaultAsync(cancellationToken)
				?? throw new PosBusinessException("COUPON_NOT_FOUND", "Coupon code was not found.", StatusCodes.Status404NotFound);
			couponDiscount = ValidateAndCalculateCoupon(promotion, subTotal - roundedManualDiscount, DateTime.UtcNow);
		}

		var availableAfterCoupon = Math.Max(0, subTotal - roundedManualDiscount - couponDiscount);
		if (pointsToRedeem > 0 && customer is null)
			throw new PosBusinessException("MEMBER_REQUIRED", "Select a member before redeeming points.");
		if (customer is not null && pointsToRedeem > customer.PointsBalance)
			throw new PosBusinessException("POINTS_INSUFFICIENT", $"Member has {customer.PointsBalance:N0} points available.", StatusCodes.Status409Conflict);

		var maxRedeemablePoints = (int)Math.Floor(availableAfterCoupon / BahtPerPointRedeemed);
		var pointsRedeemed = Math.Min(pointsToRedeem, maxRedeemablePoints);
		var loyaltyDiscount = RoundMoney(pointsRedeemed * BahtPerPointRedeemed);
		var totalDiscount = RoundMoney(roundedManualDiscount + couponDiscount + loyaltyDiscount);
		var taxableAmount = Math.Max(0, subTotal - totalDiscount);

		var tenantId = RequireTenantId();
		var tenantRates = await dbContext.Tenants
			.AsNoTracking()
			.Where(tenant => tenant.Id == tenantId)
			.Select(tenant => new { tenant.VatRate, tenant.ServiceChargeRate })
			.SingleOrDefaultAsync(cancellationToken)
			?? throw new PosBusinessException("TENANT_NOT_FOUND", "Store configuration was not found.", StatusCodes.Status404NotFound);

		var serviceCharge = RoundMoney(taxableAmount * tenantRates.ServiceChargeRate / 100m);
		var vat = RoundMoney((taxableAmount + serviceCharge) * tenantRates.VatRate / 100m);
		var total = RoundMoney(taxableAmount + serviceCharge + vat);
		var pointsEarned = customer is null ? 0 : (int)Math.Floor(total / BahtPerPointEarned);

		return new CheckoutPricing(
			lines,
			subTotal,
			roundedManualDiscount,
			couponDiscount,
			loyaltyDiscount,
			totalDiscount,
			serviceCharge,
			vat,
			total,
			customer,
			promotion,
			pointsRedeemed,
			pointsEarned);
	}

	public static decimal ValidateAndCalculateCoupon(PromotionCoupon promotion, decimal eligibleAmount, DateTime utcNow)
	{
		if (!promotion.IsActive)
			throw new PosBusinessException("COUPON_INACTIVE", "Coupon is inactive.");
		if (promotion.ValidFrom.HasValue && promotion.ValidFrom.Value > utcNow)
			throw new PosBusinessException("COUPON_NOT_STARTED", "Coupon is not valid yet.");
		if (promotion.ValidUntil.HasValue && promotion.ValidUntil.Value < utcNow)
			throw new PosBusinessException("COUPON_EXPIRED", "Coupon has expired.");
		if (promotion.UsageLimit.HasValue && promotion.UsageCount >= promotion.UsageLimit.Value)
			throw new PosBusinessException("COUPON_LIMIT_REACHED", "Coupon usage limit has been reached.");
		if (eligibleAmount < promotion.MinimumOrderAmount)
			throw new PosBusinessException("COUPON_MINIMUM_NOT_MET", $"Minimum eligible amount is {promotion.MinimumOrderAmount:N2}.");

		var discount = promotion.DiscountType switch
		{
			PromotionDiscountType.FixedAmount => promotion.Value,
			PromotionDiscountType.Percentage => eligibleAmount * promotion.Value / 100m,
			_ => 0
		};
		if (promotion.MaximumDiscountAmount.HasValue)
			discount = Math.Min(discount, promotion.MaximumDiscountAmount.Value);
		return RoundMoney(Math.Min(eligibleAmount, discount));
	}

	public static string NormalizePhone(string phone)
	{
		var digits = new string(phone.Where(char.IsDigit).ToArray());
		if (digits.StartsWith("66", StringComparison.Ordinal) && digits.Length >= 11)
			digits = $"0{digits[2..]}";
		if (digits.Length < 9 || digits.Length > 15)
			throw new PosBusinessException("PHONE_INVALID", "Phone number must contain 9 to 15 digits.");
		return digits;
	}

	public static OrderDto ToOrderDto(Order order) => new(
		order.Id,
		order.OrderNo,
		order.SubTotalAmount,
		order.ServiceChargeAmount,
		order.VatAmount,
		order.TotalAmount,
		order.DiscountAmount,
		order.ManualDiscountAmount,
		order.CouponDiscountAmount,
		order.LoyaltyDiscountAmount,
		order.PaidAmount,
		order.ChangeAmount,
		order.PaymentMethod,
		order.Status,
		order.CashierName,
		order.CustomerId,
		order.Customer?.Name,
		order.Customer?.Phone,
		order.CouponCode,
		order.LoyaltyPointsEarned,
		order.LoyaltyPointsRedeemed,
		order.CashShiftId,
		order.Version,
		order.CreatedAt,
		order.Items.Select(item => new OrderItemDto(
			item.Id,
			item.ProductId,
			item.ProductName,
			item.Barcode,
			item.UnitPrice,
			item.Quantity,
			item.SubTotal,
			item.RefundedQuantity,
			item.Quantity - item.RefundedQuantity)).ToList(),
		order.TotalRefundedAmount);

	private static OrderReversalDto ToReversalDto(OrderReversal reversal) => new(
		reversal.Id,
		reversal.OrderId,
		reversal.Order?.OrderNo ?? string.Empty,
		reversal.Type,
		reversal.Amount,
		reversal.StockRestored,
		reversal.Reason,
		reversal.IdempotencyKey,
		reversal.PerformedBy,
		reversal.ProcessedAt,
		reversal.SubTotalAmount,
		reversal.ManualDiscountAmount,
		reversal.CouponDiscountAmount,
		reversal.LoyaltyDiscountAmount,
		reversal.ServiceChargeAmount,
		reversal.VatAmount,
		reversal.IsFullOrderReversal,
		reversal.LoyaltyPointsEarnedReversed,
		reversal.LoyaltyPointsRedeemedRestored,
		reversal.CouponUsageReleased,
		reversal.Items
			.OrderBy(item => item.OrderItemId)
			.Select(item => new OrderReversalItemDto(
				item.OrderItemId,
				item.ProductId,
				item.ProductName,
				item.Quantity,
				item.SubTotalAmount,
				item.ManualDiscountAmount,
				item.CouponDiscountAmount,
				item.LoyaltyDiscountAmount,
				item.ServiceChargeAmount,
				item.VatAmount,
				item.TotalAmount))
			.ToList());

	private static OrderQuoteDto ToQuoteDto(CheckoutPricing pricing) => new(
		pricing.SubTotalAmount,
		pricing.ManualDiscountAmount,
		pricing.CouponDiscountAmount,
		pricing.LoyaltyDiscountAmount,
		pricing.TotalDiscountAmount,
		pricing.ServiceChargeAmount,
		pricing.VatAmount,
		pricing.TotalAmount,
		pricing.Customer?.Id,
		pricing.Customer?.Name,
		pricing.Customer?.PointsBalance ?? 0,
		pricing.LoyaltyPointsRedeemed,
		pricing.LoyaltyPointsEarned,
		pricing.Promotion?.Code);

	private int RequireTenantId()
	{
		if (tenantProvider.CurrentTenantId is not > 0)
			throw new PosBusinessException("TENANT_REQUIRED", "A valid tenant is required.", StatusCodes.Status401Unauthorized);
		return tenantProvider.CurrentTenantId.Value;
	}

	private static string NormalizeIdempotencyKey(string value)
	{
		var normalized = value.Trim();
		if (normalized.Length is < 8 or > 100)
			throw new PosBusinessException("IDEMPOTENCY_KEY_INVALID", "Idempotency key must be between 8 and 100 characters.");
		return normalized;
	}

	private static string CreateCheckoutFingerprint(CreateOrderDto request)
	{
		var itemPart = string.Join(",", request.Items
			.GroupBy(item => item.ProductId)
			.OrderBy(group => group.Key)
			.Select(group => $"{group.Key}:{group.Sum(item => item.Quantity)}"));
		var raw = string.Join("|",
			itemPart,
			request.DiscountAmount.ToString("0.00", CultureInfo.InvariantCulture),
			request.PaidAmount.ToString("0.00", CultureInfo.InvariantCulture),
			request.PaymentMethod,
			request.CustomerPhone?.Trim() ?? string.Empty,
			request.CouponCode?.Trim().ToUpperInvariant() ?? string.Empty,
			request.LoyaltyPointsToRedeem);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
	}

	private static string CreateReversalFingerprint(
		int orderId,
		OrderReversalType type,
		string reason,
		IReadOnlyCollection<PartialRefundOrderItemRequest>? requestedItems)
	{
		var itemPart = requestedItems is null
			? "ALL_REMAINING"
			: string.Join(",", requestedItems
				.OrderBy(item => item.OrderItemId)
				.Select(item => $"{item.OrderItemId}:{item.Quantity}"));
		var raw = string.Join("|", orderId, type, reason, itemPart);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
	}

	private static string NormalizeReversalReason(string reason)
	{
		var normalized = reason?.Trim() ?? string.Empty;
		if (normalized.Length is < 3 or > 500)
			throw new PosBusinessException("REVERSAL_REASON_INVALID", "Reversal reason must be between 3 and 500 characters.");
		return normalized;
	}

	private static void ValidatePartialRefundRequest(
		OrderReversalType type,
		IReadOnlyCollection<PartialRefundOrderItemRequest>? requestedItems)
	{
		if (type != OrderReversalType.PartialRefund)
			return;
		if (requestedItems is null || requestedItems.Count == 0)
			throw new PosBusinessException("REFUND_ITEMS_REQUIRED", "Select at least one order item to refund.");
		if (requestedItems.Any(item => item.OrderItemId <= 0 || item.Quantity <= 0))
			throw new PosBusinessException("REFUND_QUANTITY_INVALID", "Refund item IDs and quantities must be positive.");
		if (requestedItems.GroupBy(item => item.OrderItemId).Any(group => group.Count() > 1))
			throw new PosBusinessException("REFUND_ITEM_DUPLICATE", "Each order item may appear only once in a refund request.");
	}

	private static OrderReversalDto ValidateIdempotentReversal(
		OrderReversal existing,
		int orderId,
		OrderReversalType type,
		string requestFingerprint)
	{
		if (existing.OrderId != orderId ||
			existing.Type != type ||
			!string.Equals(existing.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
		{
			throw new PosBusinessException(
				"IDEMPOTENCY_KEY_REUSED",
				"The idempotency key was already used for a different reversal request.",
				StatusCodes.Status409Conflict);
		}

		return ToReversalDto(existing);
	}

	private static void ValidateOrderCanBeReversed(Order order, OrderReversalType type)
	{
		if (type == OrderReversalType.Void)
		{
			if (order.Status != OrderStatus.Completed ||
				order.TotalRefundedAmount > 0 ||
				order.Items.Any(item => item.RefundedQuantity > 0))
			{
				throw new PosBusinessException(
					"ORDER_CANNOT_BE_VOIDED",
					"Only a completed order with no prior refund can be voided.",
					StatusCodes.Status409Conflict);
			}
			return;
		}

		if (order.Status is not (OrderStatus.Completed or OrderStatus.PartiallyRefunded))
		{
			throw new PosBusinessException(
				"ORDER_CANNOT_BE_REFUNDED",
				$"Order cannot be refunded while it is {order.Status}.",
				StatusCodes.Status409Conflict);
		}
	}

	private static List<RefundRequestLine> ResolveRefundLines(
		Order order,
		OrderReversalType type,
		IReadOnlyCollection<PartialRefundOrderItemRequest>? requestedItems)
	{
		if (type != OrderReversalType.PartialRefund)
		{
			var remaining = order.Items
				.Where(item => item.Quantity > item.RefundedQuantity)
				.Select(item => new RefundRequestLine(item, item.Quantity - item.RefundedQuantity))
				.ToList();
			if (remaining.Count == 0)
				throw new PosBusinessException("ORDER_ALREADY_REVERSED", "The order has no refundable quantities.", StatusCodes.Status409Conflict);
			return remaining;
		}

		var itemLookup = order.Items.ToDictionary(item => item.Id);
		var result = new List<RefundRequestLine>(requestedItems!.Count);
		foreach (var requestItem in requestedItems)
		{
			if (!itemLookup.TryGetValue(requestItem.OrderItemId, out var orderItem))
				throw new PosBusinessException("ORDER_ITEM_NOT_FOUND", $"Order item {requestItem.OrderItemId} was not found.", StatusCodes.Status404NotFound);
			var refundableQuantity = orderItem.Quantity - orderItem.RefundedQuantity;
			if (requestItem.Quantity > refundableQuantity)
			{
				throw new PosBusinessException(
					"REFUND_QUANTITY_EXCEEDS_REMAINING",
					$"Order item {requestItem.OrderItemId} has only {refundableQuantity} refundable unit(s).",
					StatusCodes.Status409Conflict);
			}
			result.Add(new RefundRequestLine(orderItem, requestItem.Quantity));
		}
		return result;
	}

	private static Dictionary<int, OrderLineAllocation> CreateLineAllocations(Order order)
	{
		var subTotals = order.Items.ToDictionary(item => item.Id, item => item.SubTotal);
		var manualDiscounts = AllocateAcrossLines(order.Items, order.ManualDiscountAmount);
		var couponDiscounts = AllocateAcrossLines(order.Items, order.CouponDiscountAmount);
		var loyaltyDiscounts = AllocateAcrossLines(order.Items, order.LoyaltyDiscountAmount);
		var serviceCharges = AllocateAcrossLines(order.Items, order.ServiceChargeAmount);
		var vatAmounts = AllocateAcrossLines(order.Items, order.VatAmount);
		return order.Items.ToDictionary(
			item => item.Id,
			item => new OrderLineAllocation(
				subTotals[item.Id],
				manualDiscounts[item.Id],
				couponDiscounts[item.Id],
				loyaltyDiscounts[item.Id],
				serviceCharges[item.Id],
				vatAmounts[item.Id]));
	}

	private static Dictionary<int, decimal> AllocateAcrossLines(
		IEnumerable<OrderItem> sourceItems,
		decimal componentTotal)
	{
		var items = sourceItems.OrderBy(item => item.Id).ToList();
		var result = new Dictionary<int, decimal>(items.Count);
		var weightTotal = items.Sum(item => item.SubTotal);
		decimal allocated = 0;
		for (var index = 0; index < items.Count; index++)
		{
			var item = items[index];
			var amount = index == items.Count - 1
				? RoundMoney(componentTotal - allocated)
				: weightTotal == 0
					? 0
					: RoundMoney(componentTotal * item.SubTotal / weightTotal);
			result[item.Id] = amount;
			allocated += amount;
		}
		return result;
	}

	private static decimal AllocateRefundChunk(
		decimal lineComponentTotal,
		int originalQuantity,
		int alreadyRefundedQuantity,
		int refundQuantity)
	{
		static decimal Target(decimal total, int quantity, int cumulativeQuantity) =>
			cumulativeQuantity >= quantity
				? total
				: RoundMoney(total * cumulativeQuantity / quantity);

		var before = Target(lineComponentTotal, originalQuantity, alreadyRefundedQuantity);
		var after = Target(lineComponentTotal, originalQuantity, alreadyRefundedQuantity + refundQuantity);
		return RoundMoney(after - before);
	}

	private void ApplyLoyaltyReversal(
		Order order,
		OrderReversal reversal,
		bool allItemsReversed,
		int tenantId,
		DateTime now)
	{
		var previousEarnedReversed = order.Reversals.Sum(value => value.LoyaltyPointsEarnedReversed);
		var previousRedeemedRestored = order.Reversals.Sum(value => value.LoyaltyPointsRedeemedRestored);
		var targetEarnedReversed = CalculateCumulativePoints(
			order.LoyaltyPointsEarned,
			order.TotalRefundedAmount,
			order.TotalAmount,
			allItemsReversed);
		var targetRedeemedRestored = CalculateCumulativePoints(
			order.LoyaltyPointsRedeemed,
			order.TotalRefundedAmount,
			order.TotalAmount,
			allItemsReversed);
		reversal.LoyaltyPointsEarnedReversed = Math.Max(0, targetEarnedReversed - previousEarnedReversed);
		reversal.LoyaltyPointsRedeemedRestored = Math.Max(0, targetRedeemedRestored - previousRedeemedRestored);

		if (order.Customer is null)
			return;

		if (reversal.LoyaltyPointsEarnedReversed > 0)
		{
			order.Customer.PointsBalance -= reversal.LoyaltyPointsEarnedReversed;
			order.Customer.LifetimePointsEarned = Math.Max(
				0,
				order.Customer.LifetimePointsEarned - reversal.LoyaltyPointsEarnedReversed);
			dbContext.LoyaltyTransactions.Add(new LoyaltyTransaction
			{
				TenantId = tenantId,
				Customer = order.Customer,
				Order = order,
				Type = LoyaltyTransactionType.EarnReversal,
				PointsChange = -reversal.LoyaltyPointsEarnedReversed,
				BalanceAfter = order.Customer.PointsBalance,
				Description = $"{reversal.Type} earned-point reversal for {order.OrderNo}",
				CreatedAt = now
			});
		}

		if (reversal.LoyaltyPointsRedeemedRestored > 0)
		{
			order.Customer.PointsBalance += reversal.LoyaltyPointsRedeemedRestored;
			order.Customer.LifetimePointsRedeemed = Math.Max(
				0,
				order.Customer.LifetimePointsRedeemed - reversal.LoyaltyPointsRedeemedRestored);
			dbContext.LoyaltyTransactions.Add(new LoyaltyTransaction
			{
				TenantId = tenantId,
				Customer = order.Customer,
				Order = order,
				Type = LoyaltyTransactionType.RedeemReversal,
				PointsChange = reversal.LoyaltyPointsRedeemedRestored,
				BalanceAfter = order.Customer.PointsBalance,
				Description = $"{reversal.Type} redeemed-point restoration for {order.OrderNo}",
				CreatedAt = now
			});
		}

		order.Customer.UpdatedAt = now;
	}

	private static int CalculateCumulativePoints(
		int originalPoints,
		decimal cumulativeRefundAmount,
		decimal orderTotal,
		bool allItemsReversed)
	{
		if (originalPoints <= 0)
			return 0;
		if (allItemsReversed || cumulativeRefundAmount >= orderTotal)
			return originalPoints;
		if (orderTotal <= 0)
			return 0;
		return Math.Clamp(
			(int)Math.Round(
				originalPoints * cumulativeRefundAmount / orderTotal,
				0,
				MidpointRounding.AwayFromZero),
			0,
			originalPoints);
	}

	private static decimal RoundMoney(decimal value) =>
		Math.Round(value, 2, MidpointRounding.AwayFromZero);

	private static bool IsUniqueConstraint(DbUpdateException exception) =>
		exception.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
		exception.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true;

	private sealed record CheckoutLine(Product Product, int Quantity, decimal SubTotal);

	private sealed record RefundRequestLine(OrderItem OrderItem, int Quantity);

	private sealed record OrderLineAllocation(
		decimal SubTotalAmount,
		decimal ManualDiscountAmount,
		decimal CouponDiscountAmount,
		decimal LoyaltyDiscountAmount,
		decimal ServiceChargeAmount,
		decimal VatAmount);

	private sealed record CheckoutPricing(
		List<CheckoutLine> Lines,
		decimal SubTotalAmount,
		decimal ManualDiscountAmount,
		decimal CouponDiscountAmount,
		decimal LoyaltyDiscountAmount,
		decimal TotalDiscountAmount,
		decimal ServiceChargeAmount,
		decimal VatAmount,
		decimal TotalAmount,
		Customer? Customer,
		PromotionCoupon? Promotion,
		int LoyaltyPointsRedeemed,
		int LoyaltyPointsEarned);
}
