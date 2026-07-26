using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;
using SmartPos.Api.Services;

namespace SmartPos.Api.Tests;

public sealed class CurrentUserServiceTests
{
	[Fact]
	public async Task Revoked_permission_is_checked_from_database()
	{
		var fixture = await CreateFixtureAsync(isTenantActive: true, canProcessCheckout: false);
		await using var connection = fixture.Connection;
		await using var db = fixture.Db;
		var service = new CurrentUserService(db);

		var exception = await Assert.ThrowsAsync<PosBusinessException>(
			() => service.RequireAsync(fixture.Principal, user => user.CanProcessCheckout, CancellationToken.None));

		Assert.Equal("PERMISSION_DENIED", exception.Code);
	}

	[Fact]
	public async Task Inactive_tenant_revokes_existing_user_token()
	{
		var fixture = await CreateFixtureAsync(isTenantActive: false, canProcessCheckout: true);
		await using var connection = fixture.Connection;
		await using var db = fixture.Db;
		var service = new CurrentUserService(db);

		var exception = await Assert.ThrowsAsync<PosBusinessException>(
			() => service.GetRequiredAsync(fixture.Principal, CancellationToken.None));

		Assert.Equal("TENANT_INACTIVE", exception.Code);
	}

	private static async Task<TestFixture> CreateFixtureAsync(bool isTenantActive, bool canProcessCheckout)
	{
		var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		var tenantProvider = new TenantProvider();
		var options = new DbContextOptionsBuilder<SmartPosDbContext>().UseSqlite(connection).Options;
		var db = new SmartPosDbContext(options, tenantProvider);
		await db.Database.EnsureCreatedAsync();
		var tenant = new Tenant { Name = "Permission Store", Slug = Guid.NewGuid().ToString("N"), IsActive = isTenantActive };
		db.Tenants.Add(tenant);
		await db.SaveChangesAsync();
		tenantProvider.SetTenantId(tenant.Id);
		var user = new User
		{
			TenantId = tenant.Id,
			Email = $"{Guid.NewGuid():N}@example.com",
			PasswordHash = "not-used",
			FullName = "Permission User",
			Role = UserRole.Cashier,
			CanProcessCheckout = canProcessCheckout
		};
		db.Users.Add(user);
		await db.SaveChangesAsync();
		db.ChangeTracker.Clear();
		var principal = new ClaimsPrincipal(new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
			"test"));
		return new TestFixture(connection, db, principal);
	}

	private sealed record TestFixture(
		SqliteConnection Connection,
		SmartPosDbContext Db,
		ClaimsPrincipal Principal);
}
