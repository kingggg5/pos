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
[Route("api/customers")]
[Authorize]
public sealed class CustomersController(
	SmartPosDbContext dbContext,
	ITenantProvider tenantProvider,
	ICurrentUserService currentUserService) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<List<CustomerDto>>> GetCustomers(
		[FromQuery, MaxLength(160)] string? search,
		[FromQuery, Range(1, 200)] int limit = 50,
		CancellationToken cancellationToken = default)
	{
		var query = dbContext.Customers.AsNoTracking();
		if (!string.IsNullOrWhiteSpace(search))
		{
			var normalized = search.Trim().ToLower();
			var phoneDigits = new string(search.Where(char.IsDigit).ToArray());
			query = query.Where(customer =>
				customer.Name.ToLower().Contains(normalized) ||
				customer.PhoneNormalized.Contains(phoneDigits) ||
				(customer.Email != null && customer.Email.ToLower().Contains(normalized)));
		}

		var customers = await query
			.OrderBy(customer => customer.Name)
			.Take(limit)
			.ToListAsync(cancellationToken);
		return Ok(customers.Select(ToDto).ToList());
	}

	[HttpGet("search")]
	public async Task<ActionResult<CustomerDto>> SearchByPhone(
		[FromQuery, Required, MaxLength(30)] string phone,
		CancellationToken cancellationToken)
	{
		try
		{
			var normalized = CommerceService.NormalizePhone(phone);
			var customer = await dbContext.Customers
				.AsNoTracking()
				.SingleOrDefaultAsync(value => value.PhoneNormalized == normalized, cancellationToken);
			return customer is null ? NotFound() : Ok(ToDto(customer));
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	[HttpGet("{id:int}")]
	public async Task<ActionResult<CustomerDto>> GetCustomer(int id, CancellationToken cancellationToken)
	{
		var customer = await dbContext.Customers.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
		return customer is null ? NotFound() : Ok(ToDto(customer));
	}

	[HttpPost]
	public async Task<ActionResult<CustomerDto>> Create(
		SaveCustomerRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var actor = await currentUserService.RequireAsync(User, user => user.CanProcessCheckout, cancellationToken);
			var tenantId = tenantProvider.CurrentTenantId
				?? throw new PosBusinessException("TENANT_REQUIRED", "A valid tenant is required.", StatusCodes.Status401Unauthorized);
			var normalizedPhone = CommerceService.NormalizePhone(request.Phone);
			if (await dbContext.Customers.AnyAsync(customer => customer.PhoneNormalized == normalizedPhone, cancellationToken))
				throw new PosBusinessException("PHONE_ALREADY_REGISTERED", "This phone number is already registered.", StatusCodes.Status409Conflict);

			var now = DateTime.UtcNow;
			var customer = new Customer
			{
				TenantId = tenantId,
				Phone = request.Phone.Trim(),
				PhoneNormalized = normalizedPhone,
				Name = request.Name.Trim(),
				Email = NormalizeEmail(request.Email),
				CreatedAt = now,
				UpdatedAt = now
			};
			dbContext.Customers.Add(customer);
			dbContext.AuditLogs.Add(new AuditLog
			{
				TenantId = tenantId,
				Action = "CUSTOMER_CREATED",
				Category = "Customer",
				PerformedBy = actor.FullName,
				Details = $"Registered member {customer.Name} ({customer.Phone})",
				CreatedAt = now
			});
			await dbContext.SaveChangesAsync(cancellationToken);
			return Ok(ToDto(customer));
		}
		catch (DbUpdateException)
		{
			return this.ToProblem(new PosBusinessException("PHONE_ALREADY_REGISTERED", "This phone number is already registered.", StatusCodes.Status409Conflict));
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	[HttpPut("{id:int}")]
	public async Task<ActionResult<CustomerDto>> Update(
		int id,
		SaveCustomerRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var actor = await currentUserService.RequireAsync(User, user => user.CanProcessCheckout, cancellationToken);
			var customer = await dbContext.Customers.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
			if (customer is null)
				return NotFound();

			var normalizedPhone = CommerceService.NormalizePhone(request.Phone);
			if (await dbContext.Customers.AnyAsync(value => value.Id != id && value.PhoneNormalized == normalizedPhone, cancellationToken))
				throw new PosBusinessException("PHONE_ALREADY_REGISTERED", "This phone number is already registered.", StatusCodes.Status409Conflict);

			customer.Phone = request.Phone.Trim();
			customer.PhoneNormalized = normalizedPhone;
			customer.Name = request.Name.Trim();
			customer.Email = NormalizeEmail(request.Email);
			customer.UpdatedAt = DateTime.UtcNow;
			dbContext.AuditLogs.Add(new AuditLog
			{
				TenantId = customer.TenantId,
				Action = "CUSTOMER_UPDATED",
				Category = "Customer",
				PerformedBy = actor.FullName,
				Details = $"Updated member {customer.Name} ({customer.Phone})",
				CreatedAt = customer.UpdatedAt
			});
			await dbContext.SaveChangesAsync(cancellationToken);
			return Ok(ToDto(customer));
		}
		catch (DbUpdateConcurrencyException)
		{
			return this.ToProblem(new PosBusinessException("CONCURRENT_UPDATE", "Member was changed by another request.", StatusCodes.Status409Conflict));
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	[HttpGet("{id:int}/points")]
	public async Task<ActionResult<List<LoyaltyTransactionDto>>> GetPoints(
		int id,
		[FromQuery, Range(1, 200)] int limit = 50,
		CancellationToken cancellationToken = default)
	{
		if (!await dbContext.Customers.AnyAsync(customer => customer.Id == id, cancellationToken))
			return NotFound();

		var transactions = await dbContext.LoyaltyTransactions
			.AsNoTracking()
			.Where(value => value.CustomerId == id)
			.OrderByDescending(value => value.CreatedAt)
			.Take(limit)
			.Select(value => new LoyaltyTransactionDto(
				value.Id,
				value.Type,
				value.PointsChange,
				value.BalanceAfter,
				value.OrderId,
				value.Order != null ? value.Order.OrderNo : null,
				value.Description,
				value.CreatedAt))
			.ToListAsync(cancellationToken);
		return Ok(transactions);
	}

	private static CustomerDto ToDto(Customer customer) => new(
		customer.Id,
		customer.Phone,
		customer.Name,
		customer.Email,
		customer.PointsBalance,
		customer.LifetimePointsEarned,
		customer.LifetimePointsRedeemed,
		customer.CreatedAt,
		customer.UpdatedAt,
		customer.Version);

	private static string? NormalizeEmail(string? email) =>
		string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
