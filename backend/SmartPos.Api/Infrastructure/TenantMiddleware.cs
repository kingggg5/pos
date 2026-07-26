using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace SmartPos.Api.Infrastructure;

public sealed class TenantMiddleware(RequestDelegate next)
{
	public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider)
	{
		if (context.User.Identity?.IsAuthenticated == true)
		{
			var tenantClaim = context.User.FindFirst("tenant_id")?.Value;
			if (!int.TryParse(tenantClaim, out var claimTenantId) || claimTenantId <= 0)
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				await context.Response.WriteAsJsonAsync(new ProblemDetails
				{
					Status = StatusCodes.Status401Unauthorized,
					Title = "TENANT_CLAIM_REQUIRED",
					Detail = "Authenticated token does not contain a valid tenant claim."
				});
				return;
			}

			if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues) &&
				int.TryParse(tenantHeaderValues.FirstOrDefault(), out var headerTenantId) &&
				headerTenantId != claimTenantId)
			{
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				await context.Response.WriteAsJsonAsync(new ProblemDetails
				{
					Status = StatusCodes.Status403Forbidden,
					Title = "TENANT_MISMATCH",
					Detail = "X-Tenant-Id must match the tenant in the authenticated token."
				});
				return;
			}

			tenantProvider.SetTenantId(claimTenantId);
		}

		await next(context);
	}
}
