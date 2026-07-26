using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Dtos;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Services;

namespace SmartPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class StoreSettingsController(
	SmartPosDbContext dbContext,
	ITenantProvider tenantProvider,
	IAuditLogService auditLogService,
	ICurrentUserService currentUserService) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<StoreSettingsDto>> GetSettings(CancellationToken ct)
	{
		var tenantId = tenantProvider.CurrentTenantId;
		var tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
		if (tenant is null)
			return NotFound("Store not found");

		return Ok(new StoreSettingsDto(
			tenant.Id,
			tenant.Name,
			tenant.Slug,
			tenant.QrCodeUrl,
			tenant.VatRate,
			tenant.ServiceChargeRate,
			tenant.ReceiptHeaderNote ?? "Thank you for visiting!",
			tenant.ReceiptFooterNote ?? "Tax Invoice / Receipt",
			tenant.BusinessTimeZoneId
		));
	}

	[HttpPut]
	[Authorize(Roles = "Owner,Manager")]
	public async Task<ActionResult<StoreSettingsDto>> UpdateSettings([FromBody] UpdateStoreSettingsRequest request, CancellationToken ct)
	{
		await currentUserService.RequireAsync(
			User,
			user => user.Role is SmartPos.Api.Models.UserRole.Owner or SmartPos.Api.Models.UserRole.Manager,
			ct);
		var tenantId = tenantProvider.CurrentTenantId;
		var tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
		if (tenant is null)
			return NotFound("Store not found");

		tenant.QrCodeUrl = request.QrCodeUrl;
		tenant.VatRate = request.VatRate;
		tenant.ServiceChargeRate = request.ServiceChargeRate;
		tenant.ReceiptHeaderNote = request.ReceiptHeaderNote;
		tenant.ReceiptFooterNote = request.ReceiptFooterNote;
		if (request.BusinessTimeZoneId is not null)
			tenant.BusinessTimeZoneId = BusinessTimeZones.NormalizeOrThrow(request.BusinessTimeZoneId);

		await dbContext.SaveChangesAsync(ct);

		var userEmail = User.Identity?.Name ?? "Admin";
		await auditLogService.LogAsync(
			"STORE_SETTINGS_UPDATED",
			"Settings",
			$"Updated VAT ({tenant.VatRate}%), Service Charge ({tenant.ServiceChargeRate}%), time zone ({tenant.BusinessTimeZoneId}), QR Code & receipt notes",
			userEmail,
			ct);

		return Ok(new StoreSettingsDto(
			tenant.Id,
			tenant.Name,
			tenant.Slug,
			tenant.QrCodeUrl,
			tenant.VatRate,
			tenant.ServiceChargeRate,
			tenant.ReceiptHeaderNote,
			tenant.ReceiptFooterNote,
			tenant.BusinessTimeZoneId
		));
	}
}
