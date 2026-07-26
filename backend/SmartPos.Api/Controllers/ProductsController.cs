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
[Route("api/products")]
[Authorize]
public sealed class ProductsController(
	SmartPosDbContext dbContext,
	ITenantProvider tenantProvider,
	IAuditLogService auditLogService,
	ICurrentUserService currentUserService) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<List<ProductDto>>> GetProducts(
		[FromQuery] string? search,
		[FromQuery] int? categoryId,
		CancellationToken cancellationToken)
	{
		var query = dbContext.Products
			.AsNoTracking()
			.Include(p => p.Category)
			.Where(p => p.IsActive);

		if (!string.IsNullOrWhiteSpace(search))
		{
			var normalized = search.ToLower().Trim();
			query = query.Where(p => p.Name.ToLower().Contains(normalized) || p.Barcode.Contains(normalized));
		}

		if (categoryId.HasValue && categoryId.Value > 0)
		{
			query = query.Where(p => p.CategoryId == categoryId.Value);
		}

		var products = await query
			.OrderBy(p => p.Name)
			.Select(PosMappingExtensions.ToProductDtoProjection)
			.ToListAsync(cancellationToken);

		return Ok(products);
	}

	[HttpGet("categories")]
	public async Task<ActionResult<List<CategoryDto>>> GetCategories(CancellationToken cancellationToken)
	{
		var categories = await dbContext.Categories
			.AsNoTracking()
			.Select(c => new CategoryDto(c.Id, c.Name, c.Icon))
			.ToListAsync(cancellationToken);

		return Ok(categories);
	}

	[HttpPost]
	[Authorize(Roles = "Owner,Manager")]
	public Task<ActionResult<ProductDto>> CreateProduct([FromBody] SaveProductDto request, CancellationToken cancellationToken) =>
		SaveProduct(request with { Id = 0 }, cancellationToken);

	[HttpPut("{id:int}")]
	[Authorize(Roles = "Owner,Manager")]
	public Task<ActionResult<ProductDto>> UpdateProduct(
		int id,
		[FromBody] SaveProductDto request,
		CancellationToken cancellationToken)
	{
		if (request.Id > 0 && request.Id != id)
			return Task.FromResult<ActionResult<ProductDto>>(BadRequest(new { message = "Route and body product IDs must match." }));
		return SaveProduct(request with { Id = id }, cancellationToken);
	}

	private async Task<ActionResult<ProductDto>> SaveProduct(SaveProductDto request, CancellationToken cancellationToken)
	{
		await currentUserService.RequireAsync(User, user => user.CanManageProducts, cancellationToken);
		Product product;
		bool isNew = false;
		if (request.Id > 0)
		{
			var existingProduct = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
			if (existingProduct is null)
				return NotFound();
			product = existingProduct;
		}
		else
		{
			product = new Product { TenantId = tenantProvider.CurrentTenantId ?? 0, IsActive = true };
			dbContext.Products.Add(product);
			isNew = true;
		}

		if (!await dbContext.Categories.AnyAsync(category => category.Id == request.CategoryId, cancellationToken))
			return BadRequest(new { message = "Category does not belong to this store." });
		var normalizedBarcode = request.Barcode.Trim();
		if (await dbContext.Products.AnyAsync(
			value => value.Id != product.Id && value.Barcode == normalizedBarcode,
			cancellationToken))
			return Conflict(new { message = "Barcode is already used by another product in this store." });

		product.CategoryId = request.CategoryId;
		product.Barcode = normalizedBarcode;
		product.Name = request.Name.Trim();
		product.Price = request.Price;
		product.Cost = request.Cost;
		product.StockQuantity = request.StockQuantity;
		product.MinimumStock = request.MinimumStock;
		product.Unit = request.Unit.Trim();
		product.ImageUrl = request.ImageUrl;

		await dbContext.SaveChangesAsync(cancellationToken);

		await auditLogService.LogAsync(
			isNew ? "PRODUCT_CREATED" : "PRODUCT_UPDATED",
			"Product",
			$"{(isNew ? "Created" : "Updated")} product '{product.Name}' (Barcode: {product.Barcode}, Stock: {product.StockQuantity})",
			cancellationToken: cancellationToken
		);

		var dto = new ProductDto(
			product.Id,
			product.CategoryId,
			(await dbContext.Categories.FindAsync([product.CategoryId], cancellationToken))?.Name ?? "General",
			product.Barcode,
			product.Name,
			product.Price,
			product.Cost,
			product.StockQuantity,
			product.MinimumStock,
			product.Unit,
			product.ImageUrl,
			product.IsActive,
			product.StockQuantity <= product.MinimumStock
		);

		return Ok(dto);
	}

	[HttpDelete("{id:int}")]
	[Authorize(Roles = "Owner,Manager")]
	public async Task<IActionResult> DeleteProduct(int id, CancellationToken cancellationToken)
	{
		await currentUserService.RequireAsync(User, user => user.CanManageProducts, cancellationToken);
		var product = await dbContext.Products.FindAsync([id], cancellationToken);
		if (product == null) return NotFound();

		product.IsActive = false; // Soft delete
		await dbContext.SaveChangesAsync(cancellationToken);

		await auditLogService.LogAsync("PRODUCT_DELETED", "Product", $"Deleted product '{product.Name}' (Barcode: {product.Barcode})", cancellationToken: cancellationToken);

		return NoContent();
	}
}
