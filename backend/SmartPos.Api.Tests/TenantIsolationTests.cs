using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;

namespace SmartPos.Api.Tests;

public sealed class TenantIsolationTests
{
	[Fact]
	public async Task Global_filters_fail_closed_and_do_not_leak_other_tenants()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		var provider = new TenantProvider();
		var options = new DbContextOptionsBuilder<SmartPosDbContext>().UseSqlite(connection).Options;
		await using var db = new SmartPosDbContext(options, provider);
		await db.Database.EnsureCreatedAsync();

		var firstTenant = new Tenant { Name = "One", Slug = "one" };
		var secondTenant = new Tenant { Name = "Two", Slug = "two" };
		db.Tenants.AddRange(firstTenant, secondTenant);
		await db.SaveChangesAsync();

		provider.SetTenantId(firstTenant.Id);
		db.Customers.Add(new Customer { Phone = "0811111111", PhoneNormalized = "0811111111", Name = "One" });
		await db.SaveChangesAsync();
		provider.SetTenantId(secondTenant.Id);
		db.Customers.Add(new Customer { Phone = "0822222222", PhoneNormalized = "0822222222", Name = "Two" });
		await db.SaveChangesAsync();

		Assert.Single(await db.Customers.AsNoTracking().ToListAsync());
		Assert.Equal("Two", (await db.Customers.AsNoTracking().SingleAsync()).Name);

		var noTenantProvider = new TenantProvider();
		await using var noTenantDb = new SmartPosDbContext(options, noTenantProvider);
		Assert.Empty(await noTenantDb.Customers.AsNoTracking().ToListAsync());
	}
}
