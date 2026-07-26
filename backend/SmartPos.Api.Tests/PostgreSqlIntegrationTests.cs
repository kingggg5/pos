using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;
using Testcontainers.PostgreSql;

namespace SmartPos.Api.Tests;

public sealed class PostgreSqlIntegrationTests
{
	[Fact]
	[Trait("Category", "PostgreSqlIntegration")]
	public async Task Migrations_tenant_filters_and_concurrency_work_on_postgresql()
	{
		if (!string.Equals(
			Environment.GetEnvironmentVariable("RUN_POSTGRES_INTEGRATION_TESTS"),
			"true",
			StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
			.WithDatabase("smart_pos_tests")
			.WithUsername("smart_pos_tests")
			.WithPassword("smart-pos-test-password")
			.Build();
		await postgres.StartAsync();

		var options = new DbContextOptionsBuilder<SmartPosDbContext>()
			.UseNpgsql(postgres.GetConnectionString())
			.Options;

		var bootstrapTenantProvider = new TenantProvider();
		await using (var bootstrapContext = new SmartPosDbContext(options, bootstrapTenantProvider))
		{
			await bootstrapContext.Database.MigrateAsync();
			bootstrapContext.Tenants.AddRange(
				new Tenant { Name = "PostgreSQL Store A", Slug = "pg-store-a", BusinessTimeZoneId = "Asia/Bangkok" },
				new Tenant { Name = "PostgreSQL Store B", Slug = "pg-store-b", BusinessTimeZoneId = "UTC" });
			await bootstrapContext.SaveChangesAsync();
		}

		int tenantAId;
		int tenantBId;
		await using (var lookupContext = new SmartPosDbContext(options, bootstrapTenantProvider))
		{
			tenantAId = await lookupContext.Tenants
				.Where(tenant => tenant.Slug == "pg-store-a")
				.Select(tenant => tenant.Id)
				.SingleAsync();
			tenantBId = await lookupContext.Tenants
				.Where(tenant => tenant.Slug == "pg-store-b")
				.Select(tenant => tenant.Id)
				.SingleAsync();
		}

		await SeedProductAsync(options, tenantAId, "Tenant A Product", "PG-A");
		await SeedProductAsync(options, tenantBId, "Tenant B Product", "PG-B");

		var tenantAProvider = CreateTenantProvider(tenantAId);
		await using (var tenantAContext = new SmartPosDbContext(options, tenantAProvider))
		{
			Assert.Equal(["Tenant A Product"], await tenantAContext.Products.Select(product => product.Name).ToListAsync());
		}

		var tenantBProvider = CreateTenantProvider(tenantBId);
		await using (var tenantBContext = new SmartPosDbContext(options, tenantBProvider))
		{
			Assert.Equal(["Tenant B Product"], await tenantBContext.Products.Select(product => product.Name).ToListAsync());
		}

		await using var firstContext = new SmartPosDbContext(options, CreateTenantProvider(tenantAId));
		await using var secondContext = new SmartPosDbContext(options, CreateTenantProvider(tenantAId));
		var firstProduct = await firstContext.Products.SingleAsync();
		var secondProduct = await secondContext.Products.SingleAsync();

		firstProduct.StockQuantity = 4;
		secondProduct.StockQuantity = 3;
		await firstContext.SaveChangesAsync();

		await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
	}

	private static async Task SeedProductAsync(
		DbContextOptions<SmartPosDbContext> options,
		int tenantId,
		string productName,
		string barcode)
	{
		await using var context = new SmartPosDbContext(options, CreateTenantProvider(tenantId));
		var category = new Category { Name = "Integration" };
		context.Categories.Add(category);
		context.Products.Add(new Product
		{
			Category = category,
			Name = productName,
			Barcode = barcode,
			Price = 10m,
			Cost = 5m,
			StockQuantity = 5,
			MinimumStock = 1,
			Unit = "pcs",
			IsActive = true
		});
		await context.SaveChangesAsync();
	}

	private static TenantProvider CreateTenantProvider(int tenantId)
	{
		var tenantProvider = new TenantProvider();
		tenantProvider.SetTenantId(tenantId);
		return tenantProvider;
	}
}
