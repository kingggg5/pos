using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;
using SmartPos.Api.Services;

namespace SmartPos.Api.Tests;

public sealed class BusinessTimeAndZReportTests
{
	[Fact]
	public void Bangkok_business_day_uses_local_midnight_bounds()
	{
		var window = BusinessTimeZones.GetBusinessDay(
			new DateTime(2026, 7, 26, 20, 0, 0, DateTimeKind.Utc),
			"Asia/Bangkok");

		Assert.Equal("Asia/Bangkok", window.TimeZoneId);
		Assert.Equal(new DateOnly(2026, 7, 27), window.BusinessDate);
		Assert.Equal(new DateTime(2026, 7, 26, 17, 0, 0, DateTimeKind.Utc), window.StartUtc);
		Assert.Equal(new DateTime(2026, 7, 27, 17, 0, 0, DateTimeKind.Utc), window.EndUtc);
	}

	[Fact]
	public void Invalid_business_time_zone_is_rejected()
	{
		var exception = Assert.Throws<PosBusinessException>(
			() => BusinessTimeZones.NormalizeOrThrow("Moon/SeaOfTranquility"));

		Assert.Equal("TIME_ZONE_INVALID", exception.Code);
	}

	[Fact]
	public async Task Z_report_books_reversal_on_event_day_and_uses_net_item_quantities()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		var tenantProvider = new TenantProvider();
		var options = new DbContextOptionsBuilder<SmartPosDbContext>()
			.UseSqlite(connection)
			.Options;
		await using var db = new SmartPosDbContext(options, tenantProvider);
		await db.Database.EnsureCreatedAsync();

		var tenant = new Tenant
		{
			Name = "Bangkok Store",
			Slug = "bangkok-store",
			BusinessTimeZoneId = "Asia/Bangkok"
		};
		db.Tenants.Add(tenant);
		await db.SaveChangesAsync();
		tenantProvider.SetTenantId(tenant.Id);

		var category = new Category { TenantId = tenant.Id, Name = "General" };
		db.Categories.Add(category);
		await db.SaveChangesAsync();
		var returnedProduct = new Product
		{
			TenantId = tenant.Id,
			CategoryId = category.Id,
			Barcode = "RETURNED",
			Name = "Returned product",
			Price = 40,
			StockQuantity = 10
		};
		var currentProduct = new Product
		{
			TenantId = tenant.Id,
			CategoryId = category.Id,
			Barcode = "CURRENT",
			Name = "Current product",
			Price = 50,
			StockQuantity = 10
		};
		db.Products.AddRange(returnedProduct, currentProduct);
		await db.SaveChangesAsync();

		var priorOrder = CreateOrder(tenant.Id, "PRIOR", 40, new DateTime(2026, 7, 26, 16, 0, 0, DateTimeKind.Utc));
		priorOrder.Items.Add(new OrderItem
		{
			TenantId = tenant.Id,
			ProductId = returnedProduct.Id,
			ProductName = returnedProduct.Name,
			Barcode = returnedProduct.Barcode,
			UnitPrice = 40,
			Quantity = 1,
			SubTotal = 40,
			RefundedQuantity = 1
		});
		var currentOrder = CreateOrder(tenant.Id, "CURRENT", 100, new DateTime(2026, 7, 26, 19, 0, 0, DateTimeKind.Utc));
		currentOrder.Items.Add(new OrderItem
		{
			TenantId = tenant.Id,
			ProductId = currentProduct.Id,
			ProductName = currentProduct.Name,
			Barcode = currentProduct.Barcode,
			UnitPrice = 50,
			Quantity = 2,
			SubTotal = 100
		});
		db.Orders.AddRange(priorOrder, currentOrder);
		await db.SaveChangesAsync();

		var priorItem = Assert.Single(priorOrder.Items);
		var reversal = new OrderReversal
		{
			TenantId = tenant.Id,
			Order = priorOrder,
			Type = OrderReversalType.Refund,
			Amount = 40,
			SubTotalAmount = 40,
			StockRestored = true,
			IsFullOrderReversal = true,
			Reason = "Returned",
			IdempotencyKey = "z-report-refund-001",
			RequestFingerprint = new string('A', 64),
			PerformedByUserId = 1,
			PerformedBy = "Manager",
			ProcessedAt = new DateTime(2026, 7, 27, 1, 0, 0, DateTimeKind.Utc)
		};
		reversal.Items.Add(new OrderReversalItem
		{
			TenantId = tenant.Id,
			OrderItem = priorItem,
			ProductId = returnedProduct.Id,
			ProductName = returnedProduct.Name,
			Quantity = 1,
			SubTotalAmount = 40,
			TotalAmount = 40
		});
		db.OrderReversals.Add(reversal);
		db.FinancialEvents.AddRange(
			new FinancialEvent
			{
				TenantId = tenant.Id,
				Type = FinancialEventType.Sale,
				Order = priorOrder,
				SourceKey = "SALE:PRIOR",
				Amount = 40,
				PaymentMethod = PaymentMethod.PromptPay,
				OccurredAt = priorOrder.CreatedAt,
				Description = "Prior sale"
			},
			new FinancialEvent
			{
				TenantId = tenant.Id,
				Type = FinancialEventType.Sale,
				Order = currentOrder,
				SourceKey = "SALE:CURRENT",
				Amount = 100,
				PaymentMethod = PaymentMethod.PromptPay,
				OccurredAt = currentOrder.CreatedAt,
				Description = "Current sale"
			},
			new FinancialEvent
			{
				TenantId = tenant.Id,
				Type = FinancialEventType.Refund,
				Order = priorOrder,
				OrderReversal = reversal,
				SourceKey = "REVERSAL:Z-REPORT-001",
				Amount = -40,
				PaymentMethod = PaymentMethod.PromptPay,
				OccurredAt = reversal.ProcessedAt,
				Description = "Current refund"
			});
		await db.SaveChangesAsync();
		await SqliteSchemaUpgrader.UpgradeAsync(db);
		await SqliteSchemaUpgrader.UpgradeAsync(db);
		Assert.Equal(3, await db.FinancialEvents.CountAsync());

		var service = new ZReportService(
			db,
			tenantProvider,
			new FixedTimeProvider(new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero)));
		var report = await service.GetCurrentAsync(CancellationToken.None);

		Assert.Equal(new DateOnly(2026, 7, 27), report.BusinessDate);
		Assert.Equal(100m, report.TodayGrossSales);
		Assert.Equal(40m, report.TodayRefundAmount);
		Assert.Equal(0m, report.TodayVoidAmount);
		Assert.Equal(60m, report.TodayTotalRevenue);
		Assert.Equal(1, report.TodayTotalOrders);
		Assert.Equal(1, report.TodayReversalEvents);
		Assert.Equal(currentProduct.Id, report.TopSellingProducts[0].Id);
	}

	private static Order CreateOrder(int tenantId, string suffix, decimal total, DateTime createdAt) =>
		new()
		{
			TenantId = tenantId,
			OrderNo = $"ORDER-{suffix}",
			SubTotalAmount = total,
			TotalAmount = total,
			PaidAmount = total,
			PaymentMethod = PaymentMethod.PromptPay,
			Status = OrderStatus.Completed,
			CashierName = "Cashier",
			CreatedAt = createdAt
		};

	private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}
}
