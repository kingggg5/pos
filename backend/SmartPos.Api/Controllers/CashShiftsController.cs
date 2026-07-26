using System.ComponentModel.DataAnnotations;
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
[Route("api/cash-shifts")]
[Authorize]
public sealed class CashShiftsController(
	SmartPosDbContext dbContext,
	ITenantProvider tenantProvider,
	ICurrentUserService currentUserService) : ControllerBase
{
	[HttpGet("current")]
	public async Task<ActionResult<CashShiftDto?>> GetCurrent(CancellationToken cancellationToken)
	{
		var shift = await dbContext.CashShifts
			.AsNoTracking()
			.SingleOrDefaultAsync(value => value.Status == CashShiftStatus.Open, cancellationToken);
		return Ok(shift is null ? null : ToDto(shift));
	}

	[HttpGet]
	public async Task<ActionResult<List<CashShiftDto>>> GetHistory(
		[FromQuery, Range(1, 200)] int limit = 50,
		CancellationToken cancellationToken = default)
	{
		var shifts = await dbContext.CashShifts
			.AsNoTracking()
			.OrderByDescending(value => value.OpenedAt)
			.Take(limit)
			.ToListAsync(cancellationToken);
		return Ok(shifts.Select(ToDto).ToList());
	}

	[HttpPost("open")]
	public async Task<ActionResult<CashShiftDto>> Open(
		OpenCashShiftRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var actor = await currentUserService.RequireAsync(User, user => user.CanProcessCheckout, cancellationToken);
			var tenantId = tenantProvider.CurrentTenantId
				?? throw new PosBusinessException("TENANT_REQUIRED", "A valid tenant is required.", StatusCodes.Status401Unauthorized);
			var idempotencyKey = NormalizeKey(request.IdempotencyKey);

			var previous = await dbContext.CashShifts
				.AsNoTracking()
				.SingleOrDefaultAsync(shift => shift.OpenIdempotencyKey == idempotencyKey, cancellationToken);
			if (previous is not null)
				return Ok(ToDto(previous));

			if (await dbContext.CashShifts.AnyAsync(shift => shift.Status == CashShiftStatus.Open, cancellationToken))
				throw new PosBusinessException("SHIFT_ALREADY_OPEN", "A cash shift is already open for this store.", StatusCodes.Status409Conflict);

			var shift = new CashShift
			{
				TenantId = tenantId,
				Status = CashShiftStatus.Open,
				OpenSlot = 1,
				OpeningCash = request.OpeningCash,
				ExpectedCash = request.OpeningCash,
				OpenedAt = DateTime.UtcNow,
				OpenedByUserId = actor.Id,
				OpenedByName = actor.FullName,
				OpeningNote = request.OpeningNote?.Trim() ?? string.Empty,
				OpenIdempotencyKey = idempotencyKey
			};
			dbContext.CashShifts.Add(shift);
			dbContext.AuditLogs.Add(new AuditLog
			{
				TenantId = tenantId,
				Action = "CASH_SHIFT_OPENED",
				Category = "CashShift",
				PerformedBy = actor.FullName,
				Details = $"Opened cash shift with THB {shift.OpeningCash:N2}",
				CreatedAt = shift.OpenedAt
			});
			await dbContext.SaveChangesAsync(cancellationToken);
			return Ok(ToDto(shift));
		}
		catch (DbUpdateException)
		{
			return this.ToProblem(new PosBusinessException("SHIFT_ALREADY_OPEN", "A cash shift is already open or this request was already processed.", StatusCodes.Status409Conflict));
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	[HttpPost("{id:int}/close")]
	public async Task<ActionResult<CashShiftDto>> Close(
		int id,
		CloseCashShiftRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var actor = await currentUserService.RequireAsync(User, user => user.CanProcessCheckout, cancellationToken);
			var tenantId = tenantProvider.CurrentTenantId
				?? throw new PosBusinessException("TENANT_REQUIRED", "A valid tenant is required.", StatusCodes.Status401Unauthorized);
			var idempotencyKey = NormalizeKey(request.IdempotencyKey);

			var prior = await dbContext.CashShifts
				.AsNoTracking()
				.SingleOrDefaultAsync(shift => shift.CloseIdempotencyKey == idempotencyKey, cancellationToken);
			if (prior is not null)
			{
				if (prior.Id != id)
					throw new PosBusinessException("IDEMPOTENCY_KEY_REUSED", "The idempotency key belongs to another cash shift.", StatusCodes.Status409Conflict);
				return Ok(ToDto(prior));
			}

			var strategy = dbContext.Database.CreateExecutionStrategy();
			var dto = await strategy.ExecuteAsync(async () =>
			{
				await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
				var shift = await dbContext.CashShifts.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
					?? throw new PosBusinessException("SHIFT_NOT_FOUND", "Cash shift was not found.", StatusCodes.Status404NotFound);
				if (shift.Status != CashShiftStatus.Open)
					throw new PosBusinessException("SHIFT_ALREADY_CLOSED", "Cash shift is already closed.", StatusCodes.Status409Conflict);

				var now = DateTime.UtcNow;
				var cashSales = await dbContext.Orders
					.Where(order => order.CashShiftId == shift.Id && order.PaymentMethod == PaymentMethod.Cash)
					.SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0m;
				var cashRefunds = await dbContext.OrderReversals
					.Where(reversal => reversal.CashShiftId == shift.Id)
					.SumAsync(reversal => (decimal?)reversal.Amount, cancellationToken) ?? 0m;
				var expectedCash = RoundMoney(shift.OpeningCash + cashSales - cashRefunds);

				shift.CashSalesAmount = RoundMoney(cashSales);
				shift.CashRefundAmount = RoundMoney(cashRefunds);
				shift.ExpectedCash = expectedCash;
				shift.ClosingCash = request.ClosingCash;
				shift.Difference = RoundMoney(request.ClosingCash - expectedCash);
				shift.ClosedAt = now;
				shift.ClosedByUserId = actor.Id;
				shift.ClosedByName = actor.FullName;
				shift.ClosingNote = request.ClosingNote?.Trim() ?? string.Empty;
				shift.CloseIdempotencyKey = idempotencyKey;
				shift.Status = CashShiftStatus.Closed;
				shift.OpenSlot = null;

				dbContext.AuditLogs.Add(new AuditLog
				{
					TenantId = tenantId,
					Action = "CASH_SHIFT_CLOSED",
					Category = "CashShift",
					PerformedBy = actor.FullName,
					Details = $"Closed shift. Expected THB {expectedCash:N2}, counted THB {request.ClosingCash:N2}, difference THB {shift.Difference:N2}",
					CreatedAt = now
				});
				await dbContext.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				return ToDto(shift);
			});
			return Ok(dto);
		}
		catch (DbUpdateConcurrencyException)
		{
			return this.ToProblem(new PosBusinessException("CONCURRENT_UPDATE", "Cash shift changed while it was being closed.", StatusCodes.Status409Conflict));
		}
		catch (DbUpdateException)
		{
			return this.ToProblem(new PosBusinessException("DUPLICATE_OPERATION", "This close request was already processed.", StatusCodes.Status409Conflict));
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	private static CashShiftDto ToDto(CashShift shift) => new(
		shift.Id,
		shift.Status,
		shift.OpeningCash,
		shift.CashSalesAmount,
		shift.CashRefundAmount,
		shift.ExpectedCash,
		shift.ClosingCash,
		shift.Difference,
		shift.OpenedAt,
		shift.ClosedAt,
		shift.OpenedByUserId,
		shift.OpenedByName,
		shift.ClosedByUserId,
		shift.ClosedByName,
		shift.OpeningNote,
		shift.ClosingNote,
		shift.Version);

	private static string NormalizeKey(string value)
	{
		var key = value.Trim();
		if (key.Length is < 8 or > 100)
			throw new PosBusinessException("IDEMPOTENCY_KEY_INVALID", "Idempotency key must be between 8 and 100 characters.");
		return key;
	}

	private static decimal RoundMoney(decimal value) =>
		Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
