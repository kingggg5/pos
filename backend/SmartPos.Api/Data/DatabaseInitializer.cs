using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;

namespace SmartPos.Api.Data;

public static class DatabaseInitializer
{
	public static async Task InitializeAsync(IServiceProvider serviceProvider)
	{
		using var scope = serviceProvider.CreateScope();
		var dbContext = scope.ServiceProvider.GetRequiredService<SmartPosDbContext>();
		var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

		if (dbContext.Database.IsSqlite())
		{
			await dbContext.Database.EnsureCreatedAsync();
			await SqliteSchemaUpgrader.UpgradeAsync(dbContext);
		}
		else
		{
			await dbContext.Database.MigrateAsync();
		}

		if (await dbContext.Tenants.AnyAsync())
		{
			await SeedFeatureDemoDataAsync(dbContext, tenantProvider);
			return;
		}

		// Seed Store 1: Coffee Bar
		var coffeeTenant = new Tenant
		{
			Name = "Coffee Bar Central",
			Slug = "coffee-bar",
			Plan = "Pro",
			IsActive = true
		};
		dbContext.Tenants.Add(coffeeTenant);
		await dbContext.SaveChangesAsync();
		tenantProvider.SetTenantId(coffeeTenant.Id);

		var coffeeOwner = new User
		{
			TenantId = coffeeTenant.Id,
			Email = "owner@coffee.com",
			PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
			FullName = "Alex Coffee Owner",
			EmployeeCode = "001",
			PositionTitle = "Store Owner & Director",
			Role = UserRole.Owner,
			CanProcessCheckout = true,
			CanManageProducts = true,
			CanViewReports = true,
			CanManageUsers = true
		};
		var coffeeCashier = new User
		{
			TenantId = coffeeTenant.Id,
			Email = "cashier@coffee.com",
			PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
			FullName = "Sarah Barista",
			EmployeeCode = "002",
			PositionTitle = "Senior Cashier & Barista",
			Role = UserRole.Cashier,
			CanProcessCheckout = true,
			CanManageProducts = false,
			CanViewReports = false,
			CanManageUsers = false
		};
		dbContext.Users.AddRange(coffeeOwner, coffeeCashier);

		var catCoffee = new Category { TenantId = coffeeTenant.Id, Name = "Coffee", Icon = "coffee" };
		var catTea = new Category { TenantId = coffeeTenant.Id, Name = "Tea & Drinks", Icon = "cup" };
		var catBakery = new Category { TenantId = coffeeTenant.Id, Name = "Pastries", Icon = "pie" };
		dbContext.Categories.AddRange(catCoffee, catTea, catBakery);
		await dbContext.SaveChangesAsync();

		var p1 = new Product { TenantId = coffeeTenant.Id, CategoryId = catCoffee.Id, Barcode = "8850001", Name = "Hot Espresso", Price = 60.00m, Cost = 20.00m, StockQuantity = 100, MinimumStock = 10, Unit = "cup", ImageUrl = "https://images.unsplash.com/photo-1510591509098-f4fdc6d0ff04?w=400" };
		var p2 = new Product { TenantId = coffeeTenant.Id, CategoryId = catCoffee.Id, Barcode = "8850002", Name = "Iced Latte", Price = 75.00m, Cost = 25.00m, StockQuantity = 80, MinimumStock = 10, Unit = "cup", ImageUrl = "https://images.unsplash.com/photo-1517701604599-bb29b565090c?w=400" };
		var p3 = new Product { TenantId = coffeeTenant.Id, CategoryId = catTea.Id, Barcode = "8850003", Name = "Iced Matcha Latte", Price = 85.00m, Cost = 30.00m, StockQuantity = 50, MinimumStock = 10, Unit = "cup", ImageUrl = "https://images.unsplash.com/photo-1536256263959-770b48d82b0a?w=400" };
		var p4 = new Product { TenantId = coffeeTenant.Id, CategoryId = catBakery.Id, Barcode = "8850004", Name = "Butter Croissant", Price = 55.00m, Cost = 18.00m, StockQuantity = 4, MinimumStock = 5, Unit = "pcs", ImageUrl = "https://images.unsplash.com/photo-1555507036-ab1f4038808a?w=400" };
		dbContext.Products.AddRange(p1, p2, p3, p4);

		var log1 = new AuditLog { TenantId = coffeeTenant.Id, Action = "STORE_INITIALIZED", Category = "Auth", PerformedBy = "001 (Alex Coffee Owner)", Details = "Store Coffee Bar Central initialized successfully" };
		var log2 = new AuditLog { TenantId = coffeeTenant.Id, Action = "PRODUCT_CREATED", Category = "Product", PerformedBy = "001 (Alex Coffee Owner)", Details = "Created product Butter Croissant (Barcode 8850004)" };
		dbContext.AuditLogs.AddRange(log1, log2);
		await dbContext.SaveChangesAsync();

		// Seed Store 2: Bakery Express
		var bakeryTenant = new Tenant
		{
			Name = "Bakery Express",
			Slug = "bakery-express",
			Plan = "Basic",
			IsActive = true
		};
		dbContext.Tenants.Add(bakeryTenant);
		await dbContext.SaveChangesAsync();
		tenantProvider.SetTenantId(bakeryTenant.Id);

		var bakeryOwner = new User
		{
			TenantId = bakeryTenant.Id,
			Email = "owner@bakery.com",
			PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
			FullName = "Baker Bob",
			Role = UserRole.Owner
		};
		var bakeryCashier = new User
		{
			TenantId = bakeryTenant.Id,
			Email = "cashier@bakery.com",
			PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
			FullName = "Jane Baker",
			Role = UserRole.Cashier
		};
		dbContext.Users.AddRange(bakeryOwner, bakeryCashier);

		var catBread = new Category { TenantId = bakeryTenant.Id, Name = "Fresh Breads", Icon = "bread" };
		var catCakes = new Category { TenantId = bakeryTenant.Id, Name = "Cakes & Desserts", Icon = "cake" };
		dbContext.Categories.AddRange(catBread, catCakes);
		await dbContext.SaveChangesAsync();

		var bp1 = new Product { TenantId = bakeryTenant.Id, CategoryId = catBread.Id, Barcode = "8860001", Name = "Artisan Sourdough", Price = 120, Cost = 40, StockQuantity = 30, MinimumStock = 5, Unit = "loaf" };
		var bp2 = new Product { TenantId = bakeryTenant.Id, CategoryId = catCakes.Id, Barcode = "8860002", Name = "Chocolate Cake Slice", Price = 95, Cost = 35, StockQuantity = 20, MinimumStock = 5, Unit = "slice" };
		dbContext.Products.AddRange(bp1, bp2);
		await dbContext.SaveChangesAsync();

		await SeedFeatureDemoDataAsync(dbContext, tenantProvider);
	}

	private static async Task SeedFeatureDemoDataAsync(
		SmartPosDbContext dbContext,
		ITenantProvider tenantProvider)
	{
		var coffeeTenant = await dbContext.Tenants
			.AsNoTracking()
			.SingleOrDefaultAsync(tenant => tenant.Slug == "coffee-bar");
		if (coffeeTenant is null)
			return;

		tenantProvider.SetTenantId(coffeeTenant.Id);
		if (!await dbContext.Customers.AnyAsync())
		{
			var now = DateTime.UtcNow;
			var customer = new Customer
			{
				TenantId = coffeeTenant.Id,
				Phone = "081-234-5678",
				PhoneNormalized = "0812345678",
				Name = "Nok Demo Member",
				Email = "nok.member@example.com",
				PointsBalance = 350,
				LifetimePointsEarned = 350,
				CreatedAt = now.AddDays(-30),
				UpdatedAt = now
			};
			dbContext.Customers.Add(customer);
			await dbContext.SaveChangesAsync();
			dbContext.LoyaltyTransactions.Add(new LoyaltyTransaction
			{
				TenantId = coffeeTenant.Id,
				CustomerId = customer.Id,
				Type = LoyaltyTransactionType.Adjustment,
				PointsChange = 350,
				BalanceAfter = 350,
				Description = "Demo opening points balance",
				CreatedAt = now.AddDays(-30)
			});
		}

		if (!await dbContext.PromotionCoupons.AnyAsync())
		{
			var now = DateTime.UtcNow;
			dbContext.PromotionCoupons.AddRange(
				new PromotionCoupon
				{
					TenantId = coffeeTenant.Id,
					Code = "WELCOME10",
					Name = "Welcome 10%",
					Description = "10% off, capped at THB 100",
					DiscountType = PromotionDiscountType.Percentage,
					Value = 10,
					MinimumOrderAmount = 100,
					MaximumDiscountAmount = 100,
					IsActive = true,
					ValidFrom = now.AddDays(-30),
					ValidUntil = now.AddYears(1),
					CreatedAt = now,
					UpdatedAt = now
				},
				new PromotionCoupon
				{
					TenantId = coffeeTenant.Id,
					Code = "SAVE50",
					Name = "Save THB 50",
					Description = "THB 50 off orders of THB 300 or more",
					DiscountType = PromotionDiscountType.FixedAmount,
					Value = 50,
					MinimumOrderAmount = 300,
					UsageLimit = 500,
					IsActive = true,
					ValidFrom = now.AddDays(-30),
					ValidUntil = now.AddYears(1),
					CreatedAt = now,
					UpdatedAt = now
				});
		}

		if (!await dbContext.CashShifts.AnyAsync())
		{
			var openedAt = DateTime.UtcNow.Date.AddDays(-1).AddHours(1);
			dbContext.CashShifts.Add(new CashShift
			{
				TenantId = coffeeTenant.Id,
				Status = CashShiftStatus.Closed,
				OpenSlot = null,
				OpeningCash = 2_000,
				CashSalesAmount = 3_250,
				CashRefundAmount = 0,
				ExpectedCash = 5_250,
				ClosingCash = 5_250,
				Difference = 0,
				OpenedAt = openedAt,
				ClosedAt = openedAt.AddHours(9),
				OpenedByUserId = 1,
				OpenedByName = "Alex Coffee Owner",
				ClosedByUserId = 1,
				ClosedByName = "Alex Coffee Owner",
				OpeningNote = "Demo historical shift",
				ClosingNote = "Balanced",
				OpenIdempotencyKey = "seed-shift-open-coffee-001",
				CloseIdempotencyKey = "seed-shift-close-coffee-001"
			});
		}

		await dbContext.SaveChangesAsync();
	}
}
