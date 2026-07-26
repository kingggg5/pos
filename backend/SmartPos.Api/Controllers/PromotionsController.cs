using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Dtos;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;
using SmartPos.Api.Services;

namespace SmartPos.Api.Controllers;

[ApiController]
[Route("api/promotions")]
[Authorize]
public sealed class PromotionsController(
	SmartPosDbContext dbContext,
	ITenantProvider tenantProvider,
	ICurrentUserService currentUserService) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<List<PromotionDto>>> GetPromotions(CancellationToken cancellationToken)
	{
		var promotions = await dbContext.PromotionCoupons
			.AsNoTracking()
			.OrderByDescending(value => value.IsActive)
			.ThenBy(value => value.Code)
			.ToListAsync(cancellationToken);
		return Ok(promotions.Select(ToDto).ToList());
	}

	[HttpPost]
	public async Task<ActionResult<PromotionDto>> Create(
		SavePromotionRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var actor = await RequireManagerAsync(cancellationToken);
			ValidateRequest(request);
			var tenantId = tenantProvider.CurrentTenantId
				?? throw new PosBusinessException("TENANT_REQUIRED", "A valid tenant is required.", StatusCodes.Status401Unauthorized);
			var normalizedCode = NormalizeCode(request.Code);
			if (await dbContext.PromotionCoupons.AnyAsync(value => value.Code == normalizedCode, cancellationToken))
				throw new PosBusinessException("COUPON_CODE_EXISTS", "Coupon code already exists.", StatusCodes.Status409Conflict);

			var now = DateTime.UtcNow;
			var promotion = new PromotionCoupon
			{
				TenantId = tenantId,
				Code = normalizedCode,
				CreatedAt = now
			};
			Apply(promotion, request, now);
			dbContext.PromotionCoupons.Add(promotion);
			AddAudit(tenantId, actor.FullName, "PROMOTION_CREATED", $"Created promotion {promotion.Code}", now);
			await dbContext.SaveChangesAsync(cancellationToken);
			return Ok(ToDto(promotion));
		}
		catch (DbUpdateException)
		{
			return this.ToProblem(new PosBusinessException("COUPON_CODE_EXISTS", "Coupon code already exists.", StatusCodes.Status409Conflict));
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	[HttpPut("{id:int}")]
	public async Task<ActionResult<PromotionDto>> Update(
		int id,
		SavePromotionRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var actor = await RequireManagerAsync(cancellationToken);
			ValidateRequest(request);
			var promotion = await dbContext.PromotionCoupons.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
			if (promotion is null)
				return NotFound();

			var normalizedCode = NormalizeCode(request.Code);
			if (await dbContext.PromotionCoupons.AnyAsync(value => value.Id != id && value.Code == normalizedCode, cancellationToken))
				throw new PosBusinessException("COUPON_CODE_EXISTS", "Coupon code already exists.", StatusCodes.Status409Conflict);

			promotion.Code = normalizedCode;
			Apply(promotion, request, DateTime.UtcNow);
			AddAudit(promotion.TenantId, actor.FullName, "PROMOTION_UPDATED", $"Updated promotion {promotion.Code}", promotion.UpdatedAt);
			await dbContext.SaveChangesAsync(cancellationToken);
			return Ok(ToDto(promotion));
		}
		catch (DbUpdateConcurrencyException)
		{
			return this.ToProblem(new PosBusinessException("CONCURRENT_UPDATE", "Promotion changed while it was being updated.", StatusCodes.Status409Conflict));
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	[HttpDelete("{id:int}")]
	public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
	{
		try
		{
			var actor = await RequireManagerAsync(cancellationToken);
			var promotion = await dbContext.PromotionCoupons.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
			if (promotion is null)
				return NotFound();
			promotion.IsActive = false;
			promotion.UpdatedAt = DateTime.UtcNow;
			AddAudit(promotion.TenantId, actor.FullName, "PROMOTION_DEACTIVATED", $"Deactivated promotion {promotion.Code}", promotion.UpdatedAt);
			await dbContext.SaveChangesAsync(cancellationToken);
			return NoContent();
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	[HttpPost("validate")]
	public async Task<ActionResult<CouponValidationDto>> Validate(
		ValidatePromotionRequest request,
		CancellationToken cancellationToken)
	{
		var code = NormalizeCode(request.Code);
		var promotion = await dbContext.PromotionCoupons.AsNoTracking()
			.SingleOrDefaultAsync(value => value.Code == code, cancellationToken);
		if (promotion is null)
			return Ok(new CouponValidationDto(false, code, null, null, 0, "Coupon code was not found.", 0, null, null, null));

		try
		{
			var discount = CommerceService.ValidateAndCalculateCoupon(promotion, request.SubTotal, DateTime.UtcNow);
			return Ok(new CouponValidationDto(
				true,
				promotion.Code,
				promotion.DiscountType,
				promotion.Value,
				discount,
				"Coupon is valid.",
				promotion.MinimumOrderAmount,
				promotion.MaximumDiscountAmount,
				promotion.ValidFrom,
				promotion.ValidUntil));
		}
		catch (PosBusinessException exception)
		{
			return Ok(new CouponValidationDto(
				false,
				promotion.Code,
				promotion.DiscountType,
				promotion.Value,
				0,
				exception.Message,
				promotion.MinimumOrderAmount,
				promotion.MaximumDiscountAmount,
				promotion.ValidFrom,
				promotion.ValidUntil));
		}
	}

	private Task<User> RequireManagerAsync(CancellationToken cancellationToken) =>
		currentUserService.RequireAsync(User, value => value.Role is UserRole.Owner or UserRole.Manager, cancellationToken);

	private void AddAudit(int tenantId, string performedBy, string action, string details, DateTime createdAt) =>
		dbContext.AuditLogs.Add(new AuditLog
		{
			TenantId = tenantId,
			Action = action,
			Category = "Promotion",
			PerformedBy = performedBy,
			Details = details,
			CreatedAt = createdAt
		});

	private static void ValidateRequest(SavePromotionRequest request)
	{
		if (request.DiscountType == PromotionDiscountType.Percentage && request.Value > 100)
			throw new PosBusinessException("PERCENTAGE_INVALID", "Percentage discount cannot exceed 100.");
		if (request.ValidFrom.HasValue && request.ValidUntil.HasValue && request.ValidUntil <= request.ValidFrom)
			throw new PosBusinessException("VALIDITY_INVALID", "Valid-until must be later than valid-from.");
	}

	private static void Apply(PromotionCoupon promotion, SavePromotionRequest request, DateTime now)
	{
		promotion.Name = request.Name.Trim();
		promotion.Description = request.Description?.Trim() ?? string.Empty;
		promotion.DiscountType = request.DiscountType;
		promotion.Value = request.Value;
		promotion.MinimumOrderAmount = request.MinimumOrderAmount;
		promotion.MaximumDiscountAmount = request.MaximumDiscountAmount;
		promotion.UsageLimit = request.UsageLimit;
		promotion.ValidFrom = request.ValidFrom?.ToUniversalTime();
		promotion.ValidUntil = request.ValidUntil?.ToUniversalTime();
		promotion.IsActive = request.IsActive;
		promotion.UpdatedAt = now;
	}

	private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

	private static PromotionDto ToDto(PromotionCoupon value) => new(
		value.Id,
		value.Code,
		value.Name,
		value.Description,
		value.DiscountType,
		value.Value,
		value.MinimumOrderAmount,
		value.MaximumDiscountAmount,
		value.UsageLimit,
		value.UsageCount,
		value.ValidFrom,
		value.ValidUntil,
		value.IsActive,
		value.Version);
}
