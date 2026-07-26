using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Dtos;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;
using SmartPos.Api.Realtime;
using SmartPos.Api.Services;

namespace SmartPos.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(
	SmartPosDbContext dbContext,
	ICommerceService commerceService,
	ICurrentUserService currentUserService,
	IHubContext<PosHub> hubContext) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<List<OrderDto>>> GetOrders(
		[FromQuery, Range(1, 200)] int limit = 50,
		CancellationToken cancellationToken = default)
	{
		var orders = await dbContext.Orders
			.AsNoTracking()
			.Include(order => order.Customer)
			.Include(order => order.Items)
			.OrderByDescending(order => order.CreatedAt)
			.Take(limit)
			.Select(PosMappingExtensions.ToOrderDtoProjection)
			.ToListAsync(cancellationToken);
		return Ok(orders);
	}

	[HttpPost("quote")]
	public async Task<ActionResult<OrderQuoteDto>> Quote(
		CreateOrderQuoteDto request,
		CancellationToken cancellationToken)
	{
		try
		{
			var user = await currentUserService.RequireAsync(User, value => value.CanProcessCheckout, cancellationToken);
			if (request.DiscountAmount > 0 && user.Role == UserRole.Cashier)
				throw new PosBusinessException("MANUAL_DISCOUNT_FORBIDDEN", "Only a manager or owner can apply a manual discount.", StatusCodes.Status403Forbidden);
			return Ok(await commerceService.QuoteAsync(request, cancellationToken));
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	[HttpPost("checkout")]
	public async Task<ActionResult<OrderDto>> CreateCheckoutOrder(
		CreateOrderDto request,
		CancellationToken cancellationToken)
	{
		try
		{
			var user = await currentUserService.RequireAsync(User, value => value.CanProcessCheckout, cancellationToken);
			if (request.DiscountAmount > 0 && user.Role == UserRole.Cashier)
				throw new PosBusinessException("MANUAL_DISCOUNT_FORBIDDEN", "Only a manager or owner can apply a manual discount.", StatusCodes.Status403Forbidden);
			var actor = new PosActor(user.Id, user.FullName, user.Role);
			var order = await commerceService.CheckoutAsync(request, actor, cancellationToken);

			var tenantSlug = User.FindFirst("tenant_slug")?.Value;
			if (!string.IsNullOrWhiteSpace(tenantSlug))
				await hubContext.Clients.Group($"store-{tenantSlug}").SendAsync("OrderCreated", order.OrderNo, order.TotalAmount, cancellationToken);

			return Ok(order);
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	[HttpPost("{id:int}/void")]
	public Task<ActionResult<OrderReversalDto>> VoidOrder(
		int id,
		ReverseOrderRequest request,
		CancellationToken cancellationToken) =>
		ReverseOrder(id, OrderReversalType.Void, request, cancellationToken);

	[HttpPost("{id:int}/refund")]
	public Task<ActionResult<OrderReversalDto>> RefundOrder(
		int id,
		ReverseOrderRequest request,
		CancellationToken cancellationToken) =>
		ReverseOrder(id, OrderReversalType.Refund, request, cancellationToken);

	[HttpPost("{id:int}/refund-items")]
	public async Task<ActionResult<OrderReversalDto>> RefundOrderItems(
		int id,
		PartialRefundOrderRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var user = await currentUserService.RequireAsync(
				User,
				value => value.Role is UserRole.Owner or UserRole.Manager,
				cancellationToken);
			var actor = new PosActor(user.Id, user.FullName, user.Role);
			return Ok(await commerceService.RefundItemsAsync(id, request, actor, cancellationToken));
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	[HttpGet("export")]
	public async Task<IActionResult> ExportOrdersCsv(CancellationToken cancellationToken)
	{
		await currentUserService.RequireAsync(User, user => user.CanViewReports, cancellationToken);
		var orders = await dbContext.Orders
			.AsNoTracking()
			.OrderByDescending(order => order.CreatedAt)
			.ToListAsync(cancellationToken);

		var csvBuilder = new StringBuilder();
		csvBuilder.AppendLine("ID,OrderNo,Subtotal,ServiceCharge,VAT,Discount,Total,Paid,Change,PaymentMethod,Status,CashierName,CreatedAt");
		foreach (var order in orders)
		{
			var safeOrderNo = EscapeCsv(order.OrderNo);
			var safeCashier = EscapeCsv(order.CashierName);
			csvBuilder.AppendLine($"{order.Id},{safeOrderNo},{order.SubTotalAmount:.00},{order.ServiceChargeAmount:.00},{order.VatAmount:.00},{order.DiscountAmount:.00},{order.TotalAmount:.00},{order.PaidAmount:.00},{order.ChangeAmount:.00},{order.PaymentMethod},{order.Status},{safeCashier},{order.CreatedAt:O}");
		}

		var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csvBuilder.ToString())).ToArray();
		return File(bytes, "text/csv", $"pos-orders-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
	}

	private async Task<ActionResult<OrderReversalDto>> ReverseOrder(
		int id,
		OrderReversalType type,
		ReverseOrderRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var user = await currentUserService.RequireAsync(
				User,
				value => value.Role is UserRole.Owner or UserRole.Manager,
				cancellationToken);
			var actor = new PosActor(user.Id, user.FullName, user.Role);
			return Ok(await commerceService.ReverseOrderAsync(id, type, request, actor, cancellationToken));
		}
		catch (PosBusinessException exception)
		{
			return this.ToProblem(exception);
		}
	}

	private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
