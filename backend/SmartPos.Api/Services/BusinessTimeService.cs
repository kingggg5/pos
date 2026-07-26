using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Dtos;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;

namespace SmartPos.Api.Services;

public sealed record BusinessDayWindow(
	string TimeZoneId,
	DateOnly BusinessDate,
	DateTime StartUtc,
	DateTime EndUtc);

public static class BusinessTimeZones
{
	public const string DefaultId = "Asia/Bangkok";

	public static string NormalizeOrThrow(string timeZoneId)
	{
		if (string.IsNullOrWhiteSpace(timeZoneId))
			throw new PosBusinessException("TIME_ZONE_REQUIRED", "Business time zone is required.");

		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim()).Id;
		}
		catch (TimeZoneNotFoundException)
		{
			throw new PosBusinessException("TIME_ZONE_INVALID", $"Time zone '{timeZoneId}' was not found.");
		}
		catch (InvalidTimeZoneException)
		{
			throw new PosBusinessException("TIME_ZONE_INVALID", $"Time zone '{timeZoneId}' is invalid.");
		}
	}

	public static BusinessDayWindow GetBusinessDay(DateTime utcNow, string? configuredTimeZoneId)
	{
		var normalizedUtc = utcNow.Kind == DateTimeKind.Utc
			? utcNow
			: DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
		TimeZoneInfo timeZone;
		try
		{
			timeZone = TimeZoneInfo.FindSystemTimeZoneById(
				string.IsNullOrWhiteSpace(configuredTimeZoneId) ? DefaultId : configuredTimeZoneId);
		}
		catch (TimeZoneNotFoundException)
		{
			timeZone = TimeZoneInfo.FindSystemTimeZoneById(DefaultId);
		}
		catch (InvalidTimeZoneException)
		{
			timeZone = TimeZoneInfo.FindSystemTimeZoneById(DefaultId);
		}

		var localNow = TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, timeZone);
		var localStart = DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified);
		var localEnd = localStart.AddDays(1);
		return new BusinessDayWindow(
			timeZone.Id,
			DateOnly.FromDateTime(localStart),
			TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone),
			TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone));
	}
}

public interface IZReportService
{
	Task<ZReportSummaryDto> GetCurrentAsync(CancellationToken cancellationToken);
}

public sealed class ZReportService(
	SmartPosDbContext dbContext,
	ITenantProvider tenantProvider,
	TimeProvider timeProvider) : IZReportService
{
	public async Task<ZReportSummaryDto> GetCurrentAsync(CancellationToken cancellationToken)
	{
		var tenantId = tenantProvider.CurrentTenantId
			?? throw new PosBusinessException("TENANT_REQUIRED", "A valid tenant is required.", StatusCodes.Status401Unauthorized);
		var configuredTimeZone = await dbContext.Tenants
			.AsNoTracking()
			.Where(tenant => tenant.Id == tenantId)
			.Select(tenant => tenant.BusinessTimeZoneId)
			.SingleOrDefaultAsync(cancellationToken);
		var businessDay = BusinessTimeZones.GetBusinessDay(
			timeProvider.GetUtcNow().UtcDateTime,
			configuredTimeZone);

		var events = await dbContext.FinancialEvents
			.AsNoTracking()
			.Where(financialEvent =>
				financialEvent.OccurredAt >= businessDay.StartUtc &&
				financialEvent.OccurredAt < businessDay.EndUtc)
			.ToListAsync(cancellationToken);

		var grossSales = events
			.Where(financialEvent => financialEvent.Type == FinancialEventType.Sale)
			.Sum(financialEvent => financialEvent.Amount);
		var refundAmount = -events
			.Where(financialEvent => financialEvent.Type == FinancialEventType.Refund)
			.Sum(financialEvent => financialEvent.Amount);
		var voidAmount = -events
			.Where(financialEvent => financialEvent.Type == FinancialEventType.Void)
			.Sum(financialEvent => financialEvent.Amount);
		var netRevenue = events.Sum(financialEvent => financialEvent.Amount);
		var saleEvents = events.Count(financialEvent => financialEvent.Type == FinancialEventType.Sale);
		var reversalEvents = events.Count - saleEvents;

		var allProducts = await dbContext.Products
			.AsNoTracking()
			.Include(product => product.Category)
			.ToListAsync(cancellationToken);
		var soldQuantities = await dbContext.OrderItems
			.AsNoTracking()
			.Where(item =>
				item.Order != null &&
				item.Order.CreatedAt >= businessDay.StartUtc &&
				item.Order.CreatedAt < businessDay.EndUtc)
			.GroupBy(item => item.ProductId)
			.Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
			.ToDictionaryAsync(value => value.ProductId, value => value.Quantity, cancellationToken);
		var reversedQuantities = await dbContext.OrderReversalItems
			.AsNoTracking()
			.Where(item =>
				item.OrderReversal != null &&
				item.OrderReversal.ProcessedAt >= businessDay.StartUtc &&
				item.OrderReversal.ProcessedAt < businessDay.EndUtc)
			.GroupBy(item => item.ProductId)
			.Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
			.ToDictionaryAsync(value => value.ProductId, value => value.Quantity, cancellationToken);
		var topSelling = allProducts
			.OrderByDescending(product =>
				soldQuantities.GetValueOrDefault(product.Id) -
				reversedQuantities.GetValueOrDefault(product.Id))
			.ThenBy(product => product.Name)
			.Take(5)
			.Select(product => new ProductDto(
				product.Id,
				product.CategoryId,
				product.Category?.Name ?? "General",
				product.Barcode,
				product.Name,
				product.Price,
				product.Cost,
				product.StockQuantity,
				product.MinimumStock,
				product.Unit,
				product.ImageUrl,
				product.IsActive,
				product.StockQuantity <= product.MinimumStock))
			.ToList();

		return new ZReportSummaryDto(
			netRevenue,
			saleEvents,
			allProducts.Count,
			allProducts.Count(product => product.StockQuantity <= product.MinimumStock),
			saleEvents > 0 ? (double)(grossSales / saleEvents) : 0,
			topSelling,
			businessDay.TimeZoneId,
			businessDay.BusinessDate,
			businessDay.StartUtc,
			businessDay.EndUtc,
			grossSales,
			refundAmount,
			voidAmount,
			reversalEvents);
	}
}
