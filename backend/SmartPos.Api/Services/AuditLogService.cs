using SmartPos.Api.Data;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;

namespace SmartPos.Api.Services;

public interface IAuditLogService
{
	Task LogAsync(string action, string category, string details, string performedBy = "", CancellationToken cancellationToken = default);
}

public sealed class AuditLogService(SmartPosDbContext dbContext, ITenantProvider tenantProvider) : IAuditLogService
{
	public async Task LogAsync(string action, string category, string details, string performedBy = "", CancellationToken cancellationToken = default)
	{
		var tenantId = tenantProvider.CurrentTenantId ?? 0;
		if (tenantId <= 0) return;

		var log = new AuditLog
		{
			TenantId = tenantId,
			Action = action,
			Category = category,
			PerformedBy = string.IsNullOrWhiteSpace(performedBy) ? "System / Cashier" : performedBy,
			Details = details,
			CreatedAt = DateTime.UtcNow
		};

		dbContext.AuditLogs.Add(log);
		await dbContext.SaveChangesAsync(cancellationToken);
	}
}
