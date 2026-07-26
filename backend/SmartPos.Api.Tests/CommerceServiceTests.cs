using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Dtos;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;
using SmartPos.Api.Services;

namespace SmartPos.Api.Tests;

public sealed class CommerceServiceTests : IAsyncLifetime
{
	private readonly SqliteConnection _connection = new("Data Source=:memory:");
	private readonly TenantProvider _tenantProvider = new();
	private SmartPosDbContext _db = null!;
	private CommerceService _service = null!;
	private Product _product = null!;
	private Customer _customer = null!;
	private PromotionCoupon _coupon = null!;
	private CashShift _shift = null!;

	public async Task InitializeAsync()
	{
		await _connection.OpenAsync();
		var options = new DbContextOptionsBuilder<SmartPosDbContext>()
			.UseSqlite(_connection)
			.Options;
		_db = new SmartPosDbContext(options, _tenantProvider);
		await _db.Database.EnsureCreatedAsync();

		var tenant = new Tenant
		{
			Name = "Test Store",
			Slug = "test-store",
			VatRate = 7,
			ServiceChargeRate = 10
		};
		_db.Tenants.Add(tenant);
		await _db.SaveChangesAsync();
		_tenantProvider.SetTenantId(tenant.Id);

		var category = new Category { TenantId = tenant.Id, Name = "Coffee" };
		_db.Categories.Add(category);
		await _db.SaveChangesAsync();

		_product = new Product
		{
			TenantId = tenant.Id,
			CategoryId = category.Id,
			Barcode = "T-001",
			Name = "Test Latte",
			Price = 100,
			Cost = 20,
			StockQuantity = 10
		};
		_customer = new Customer
		{
			TenantId = tenant.Id,
			Phone = "081-111-1111",
			PhoneNormalized = "0811111111",
			Name = "Test Member",
			PointsBalance = 100,
			LifetimePointsEarned = 100
		};
		_coupon = new PromotionCoupon
		{
			TenantId = tenant.Id,
			Code = "TENOFF",
			Name = "Ten percent",
			DiscountType = PromotionDiscountType.Percentage,
			Value = 10,
			IsActive = true
		};
		_shift = new CashShift
		{
			TenantId = tenant.Id,
			Status = CashShiftStatus.Open,
			OpenSlot = 1,
			OpeningCash = 500,
			ExpectedCash = 500,
			OpenedByUserId = 1,
			OpenedByName = "Manager",
			OpenIdempotencyKey = "test-open-shift-001"
		};
		_db.AddRange(_product, _customer, _coupon, _shift);
		await _db.SaveChangesAsync();
		_service = new CommerceService(_db, _tenantProvider);
	}

	public async Task DisposeAsync()
	{
		await _db.DisposeAsync();
		await _connection.DisposeAsync();
	}

	[Fact]
	public async Task Checkout_and_refund_are_atomic_tenant_scoped_and_idempotent()
	{
		var checkoutRequest = new CreateOrderDto(
			[new CreateOrderItemDto(_product.Id, 2)],
			0,
			153.01m,
			PaymentMethod.Cash,
			"checkout-test-key-001",
			_customer.Phone,
			_coupon.Code,
			50);
		var actor = new PosActor(1, "Manager", UserRole.Manager);

		var order = await _service.CheckoutAsync(checkoutRequest, actor, CancellationToken.None);
		var replay = await _service.CheckoutAsync(checkoutRequest, actor, CancellationToken.None);

		Assert.Equal(order.Id, replay.Id);
		Assert.Equal(200m, order.SubTotalAmount);
		Assert.Equal(20m, order.CouponDiscountAmount);
		Assert.Equal(50m, order.LoyaltyDiscountAmount);
		Assert.Equal(13m, order.ServiceChargeAmount);
		Assert.Equal(10.01m, order.VatAmount);
		Assert.Equal(153.01m, order.TotalAmount);
		Assert.Equal(15, order.LoyaltyPointsEarned);
		Assert.Equal(1, await _db.Orders.CountAsync());
		Assert.Equal(8, (await _db.Products.SingleAsync()).StockQuantity);
		Assert.Equal(65, (await _db.Customers.SingleAsync()).PointsBalance);
		Assert.Equal(1, (await _db.PromotionCoupons.SingleAsync()).UsageCount);
		Assert.Equal(653.01m, (await _db.CashShifts.SingleAsync()).ExpectedCash);

		var refundRequest = new ReverseOrderRequest("Customer returned full order", "refund-test-key-001");
		var reversal = await _service.ReverseOrderAsync(order.Id, OrderReversalType.Refund, refundRequest, actor, CancellationToken.None);
		var reversalReplay = await _service.ReverseOrderAsync(order.Id, OrderReversalType.Refund, refundRequest, actor, CancellationToken.None);

		Assert.Equal(reversal.Id, reversalReplay.Id);
		Assert.True(reversal.StockRestored);
		Assert.Equal(10, (await _db.Products.SingleAsync()).StockQuantity);
		Assert.Equal(100, (await _db.Customers.SingleAsync()).PointsBalance);
		Assert.Equal(0, (await _db.PromotionCoupons.SingleAsync()).UsageCount);
		Assert.Equal(500m, (await _db.CashShifts.SingleAsync()).ExpectedCash);
		Assert.Equal(OrderStatus.Refunded, (await _db.Orders.SingleAsync()).Status);
		Assert.Equal(1, await _db.OrderReversals.CountAsync());
	}

	[Fact]
	public async Task Reusing_checkout_key_for_different_request_is_rejected()
	{
		var actor = new PosActor(1, "Manager", UserRole.Manager);
		var first = new CreateOrderDto(
			[new CreateOrderItemDto(_product.Id, 1)],
			0,
			117.70m,
			PaymentMethod.Cash,
			"checkout-reuse-key-001");
		await _service.CheckoutAsync(first, actor, CancellationToken.None);
		var changed = first with { PaidAmount = 200 };

		var exception = await Assert.ThrowsAsync<PosBusinessException>(
			() => _service.CheckoutAsync(changed, actor, CancellationToken.None));

		Assert.Equal("IDEMPOTENCY_KEY_REUSED", exception.Code);
		Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
	}

	[Fact]
	public async Task Partial_refunds_are_quantity_safe_cent_exact_and_idempotent()
	{
		var actor = new PosActor(1, "Manager", UserRole.Manager);
		var order = await _service.CheckoutAsync(
			new CreateOrderDto(
				[new CreateOrderItemDto(_product.Id, 2)],
				0,
				153.01m,
				PaymentMethod.Cash,
				"checkout-partial-key-001",
				_customer.Phone,
				_coupon.Code,
				50),
			actor,
			CancellationToken.None);
		var orderItemId = Assert.Single(order.Items).Id;

		var firstRequest = new PartialRefundOrderRequest(
			[new PartialRefundOrderItemRequest(orderItemId, 1)],
			"Returned one item",
			"refund-partial-key-001");
		var first = await _service.RefundItemsAsync(order.Id, firstRequest, actor, CancellationToken.None);
		var firstReplay = await _service.RefundItemsAsync(order.Id, firstRequest, actor, CancellationToken.None);

		Assert.Equal(first.Id, firstReplay.Id);
		Assert.Equal(76.51m, first.Amount);
		Assert.Equal(100m, first.SubTotalAmount);
		Assert.Equal(10m, first.CouponDiscountAmount);
		Assert.Equal(25m, first.LoyaltyDiscountAmount);
		Assert.Equal(6.50m, first.ServiceChargeAmount);
		Assert.Equal(5.01m, first.VatAmount);
		Assert.False(first.IsFullOrderReversal);
		Assert.Equal(8, first.LoyaltyPointsEarnedReversed);
		Assert.Equal(25, first.LoyaltyPointsRedeemedRestored);
		Assert.False(first.CouponUsageReleased);
		Assert.Equal(OrderStatus.PartiallyRefunded, (await _db.Orders.SingleAsync()).Status);
		Assert.Equal(1, (await _db.OrderItems.SingleAsync()).RefundedQuantity);
		Assert.Equal(9, (await _db.Products.SingleAsync()).StockQuantity);
		Assert.Equal(1, (await _db.PromotionCoupons.SingleAsync()).UsageCount);
		Assert.Equal(1, await _db.OrderReversals.CountAsync());

		var second = await _service.RefundItemsAsync(
			order.Id,
			new PartialRefundOrderRequest(
				[new PartialRefundOrderItemRequest(orderItemId, 1)],
				"Returned final item",
				"refund-partial-key-002"),
			actor,
			CancellationToken.None);

		Assert.Equal(76.50m, second.Amount);
		Assert.Equal(5.00m, second.VatAmount);
		Assert.True(second.IsFullOrderReversal);
		Assert.Equal(7, second.LoyaltyPointsEarnedReversed);
		Assert.Equal(25, second.LoyaltyPointsRedeemedRestored);
		Assert.True(second.CouponUsageReleased);
		Assert.Equal(153.01m, first.Amount + second.Amount);
		Assert.Equal(OrderStatus.Refunded, (await _db.Orders.SingleAsync()).Status);
		Assert.Equal(153.01m, (await _db.Orders.SingleAsync()).TotalRefundedAmount);
		Assert.Equal(2, (await _db.OrderItems.SingleAsync()).RefundedQuantity);
		Assert.Equal(10, (await _db.Products.SingleAsync()).StockQuantity);
		Assert.Equal(100, (await _db.Customers.SingleAsync()).PointsBalance);
		Assert.Equal(0, (await _db.PromotionCoupons.SingleAsync()).UsageCount);
		Assert.NotNull((await _db.CouponRedemptions.SingleAsync()).ReversedAt);
		Assert.Equal(0m, await _db.FinancialEvents.SumAsync(value => value.Amount));
		Assert.Equal(3, await _db.FinancialEvents.CountAsync());
	}

	[Fact]
	public async Task Partial_refund_rejects_quantity_above_remaining_without_mutation()
	{
		var actor = new PosActor(1, "Manager", UserRole.Manager);
		var order = await _service.CheckoutAsync(
			new CreateOrderDto(
				[new CreateOrderItemDto(_product.Id, 1)],
				0,
				117.70m,
				PaymentMethod.Cash,
				"checkout-over-refund-001"),
			actor,
			CancellationToken.None);
		var item = Assert.Single(order.Items);

		var exception = await Assert.ThrowsAsync<PosBusinessException>(() =>
			_service.RefundItemsAsync(
				order.Id,
				new PartialRefundOrderRequest(
					[new PartialRefundOrderItemRequest(item.Id, 2)],
					"Too many items",
					"refund-over-limit-001"),
				actor,
				CancellationToken.None));

		Assert.Equal("REFUND_QUANTITY_EXCEEDS_REMAINING", exception.Code);
		Assert.Equal(9, (await _db.Products.SingleAsync()).StockQuantity);
		Assert.Equal(0, (await _db.OrderItems.SingleAsync()).RefundedQuantity);
		Assert.Equal(OrderStatus.Completed, (await _db.Orders.SingleAsync()).Status);
		Assert.Empty(await _db.OrderReversals.ToListAsync());
		Assert.Single(await _db.FinancialEvents.ToListAsync());
	}

	[Fact]
	public async Task Partial_refund_cannot_access_another_tenants_order()
	{
		var actor = new PosActor(1, "Manager", UserRole.Manager);
		var order = await _service.CheckoutAsync(
			new CreateOrderDto(
				[new CreateOrderItemDto(_product.Id, 1)],
				0,
				117.70m,
				PaymentMethod.Cash,
				"checkout-tenant-scope-001"),
			actor,
			CancellationToken.None);
		var item = Assert.Single(order.Items);
		var otherTenant = new Tenant { Name = "Other Store", Slug = "other-store" };
		_db.Tenants.Add(otherTenant);
		await _db.SaveChangesAsync();
		_tenantProvider.SetTenantId(otherTenant.Id);

		var exception = await Assert.ThrowsAsync<PosBusinessException>(() =>
			_service.RefundItemsAsync(
				order.Id,
				new PartialRefundOrderRequest(
					[new PartialRefundOrderItemRequest(item.Id, 1)],
					"Cross tenant attempt",
					"refund-tenant-scope-001"),
				actor,
				CancellationToken.None));

		Assert.Equal("ORDER_NOT_FOUND", exception.Code);
		_tenantProvider.SetTenantId(_product.TenantId);
		Assert.Equal(9, (await _db.Products.SingleAsync()).StockQuantity);
		Assert.Empty(await _db.OrderReversals.ToListAsync());
		Assert.Equal(117.70m, await _db.FinancialEvents.SumAsync(value => value.Amount));
	}
}
