using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Dtos;
using SmartPos.Api.Models;
using SmartPos.Api.Services;

namespace SmartPos.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public sealed class ReportsController(
	SmartPosDbContext dbContext,
	ICurrentUserService currentUserService,
	IZReportService zReportService) : ControllerBase
{
	[HttpGet("summary")]
	[Authorize(Roles = "Owner,Manager")]
	public async Task<ActionResult<ZReportSummaryDto>> GetZReportSummary(CancellationToken cancellationToken)
	{
		await currentUserService.RequireAsync(User, value => value.CanViewReports, cancellationToken);
		return Ok(await zReportService.GetCurrentAsync(cancellationToken));
	}

	[HttpGet("audit-logs")]
	[Authorize(Roles = "Owner,Manager")]
	public async Task<ActionResult<List<AuditLogDto>>> GetAuditLogs([FromQuery] int limit = 100, CancellationToken cancellationToken = default)
	{
		await currentUserService.RequireAsync(User, value => value.CanViewReports, cancellationToken);
		var logs = await dbContext.AuditLogs
			.AsNoTracking()
			.OrderByDescending(l => l.CreatedAt)
			.Take(limit)
			.Select(l => new AuditLogDto(
				l.Id,
				l.Action,
				l.Category,
				l.PerformedBy,
				l.Details,
				l.CreatedAt
			))
			.ToListAsync(cancellationToken);

		return Ok(logs);
	}

	[HttpGet("platform-dashboard")]
	public async Task<ActionResult<PlatformDashboardDto>> GetPlatformDashboard(CancellationToken cancellationToken)
	{
		if (!User.HasClaim("platform_admin", "true"))
			return Forbid();

		// Query across all tenants by disabling Global Query Filters temporarily for platform admin
		var tenants = await dbContext.Tenants
			.AsNoTracking()
			.IgnoreQueryFilters()
			.Include(t => t.Users)
			.Include(t => t.Products)
			.Include(t => t.Orders)
			.ToListAsync(cancellationToken);

		var totalStoresCount = tenants.Count;
		var completedOrders = tenants.SelectMany(t => t.Orders).Where(order => order.Status == OrderStatus.Completed).ToList();
		var totalPlatformRevenue = completedOrders.Sum(o => o.TotalAmount);
		var totalProductsCount = tenants.SelectMany(t => t.Products).Count();
		var totalOrdersCount = completedOrders.Count;

		var storeSummaries = tenants.Select(t => new StoreSummaryDto(
			t.Id,
			t.Name,
			t.Slug,
			t.Plan,
			t.Users.Count,
			t.Products.Count,
			t.Orders.Count(o => o.Status == OrderStatus.Completed),
			t.Orders.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.TotalAmount),
			t.CreatedAt
		)).ToList();

		var allProducts = tenants.SelectMany(t => t.Products).ToList();
		var categories = await dbContext.Categories
			.AsNoTracking()
			.IgnoreQueryFilters()
			.ToListAsync(cancellationToken);

		var soldByCategory = await dbContext.OrderItems
			.AsNoTracking()
			.IgnoreQueryFilters()
			.Where(item => item.Order != null && item.Order.Status == OrderStatus.Completed)
			.GroupBy(item => item.Product != null ? item.Product.CategoryId : 0)
			.Select(group => new { CategoryId = group.Key, Quantity = group.Sum(item => item.Quantity) })
			.ToDictionaryAsync(value => value.CategoryId, value => value.Quantity, cancellationToken);
		var categoryDistributions = categories
			.GroupBy(c => c.Name)
			.Select(g => new CategoryDistributionDto(
				g.Key,
				g.Sum(c => allProducts.Count(p => p.CategoryId == c.Id)),
				g.Sum(c => soldByCategory.GetValueOrDefault(c.Id))
			))
			.ToList();

		var recentLogs = await dbContext.AuditLogs
			.AsNoTracking()
			.IgnoreQueryFilters()
			.OrderByDescending(l => l.CreatedAt)
			.Take(50)
			.Select(l => new AuditLogDto(
				l.Id,
				l.Action,
				l.Category,
				l.PerformedBy,
				l.Details,
				l.CreatedAt
			))
			.ToListAsync(cancellationToken);

		var totalUsersCount = tenants.SelectMany(t => t.Users).Count();
		var totalVisitsCount = 0;

		return Ok(new PlatformDashboardDto(
			totalStoresCount,
			totalPlatformRevenue,
			totalProductsCount,
			totalOrdersCount,
			totalUsersCount,
			totalVisitsCount,
			storeSummaries,
			categoryDistributions,
			recentLogs
		));
	}
}
