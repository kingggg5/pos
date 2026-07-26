using System.Linq.Expressions;
using SmartPos.Api.Dtos;
using SmartPos.Api.Models;

namespace SmartPos.Api.Infrastructure;

public static class PosMappingExtensions
{
	public static Expression<Func<Product, ProductDto>> ToProductDtoProjection =>
		product => new ProductDto(
			product.Id,
			product.CategoryId,
			product.Category != null ? product.Category.Name : "General",
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

	public static Expression<Func<Order, OrderDto>> ToOrderDtoProjection =>
		order => new OrderDto(
			order.Id,
			order.OrderNo,
			order.SubTotalAmount,
			order.ServiceChargeAmount,
			order.VatAmount,
			order.TotalAmount,
			order.DiscountAmount,
			order.ManualDiscountAmount,
			order.CouponDiscountAmount,
			order.LoyaltyDiscountAmount,
			order.PaidAmount,
			order.ChangeAmount,
			order.PaymentMethod,
			order.Status,
			order.CashierName,
			order.CustomerId,
			order.Customer != null ? order.Customer.Name : null,
			order.Customer != null ? order.Customer.Phone : null,
			order.CouponCode,
			order.LoyaltyPointsEarned,
			order.LoyaltyPointsRedeemed,
			order.CashShiftId,
			order.Version,
			order.CreatedAt,
			order.Items.Select(item => new OrderItemDto(
				item.Id,
				item.ProductId,
				item.ProductName,
				item.Barcode,
				item.UnitPrice,
				item.Quantity,
				item.SubTotal,
				item.RefundedQuantity,
				item.Quantity - item.RefundedQuantity
			)).ToList(),
			order.TotalRefundedAmount
		);
}
