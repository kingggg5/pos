using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartPos.Api.Data;
using SmartPos.Api.Models;

namespace SmartPos.Api.Services;

public interface ICurrentUserService
{
	Task<User> GetRequiredAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
	Task<User> RequireAsync(ClaimsPrincipal principal, Func<User, bool> permission, CancellationToken cancellationToken);
}

public sealed class CurrentUserService(SmartPosDbContext dbContext) : ICurrentUserService
{
	public async Task<User> GetRequiredAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
	{
		var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
		if (!int.TryParse(userIdValue, out var userId))
			throw new PosBusinessException("USER_REQUIRED", "Authenticated user was not found.", StatusCodes.Status401Unauthorized);

		var user = await dbContext.Users
			.Include(value => value.Tenant)
			.SingleOrDefaultAsync(value => value.Id == userId, cancellationToken)
			?? throw new PosBusinessException("USER_REQUIRED", "Authenticated user was not found in this store.", StatusCodes.Status401Unauthorized);
		if (user.Tenant is null || !user.Tenant.IsActive)
			throw new PosBusinessException("TENANT_INACTIVE", "Store is inactive.", StatusCodes.Status403Forbidden);
		return user;
	}

	public async Task<User> RequireAsync(
		ClaimsPrincipal principal,
		Func<User, bool> permission,
		CancellationToken cancellationToken)
	{
		var user = await GetRequiredAsync(principal, cancellationToken);
		if (!permission(user))
			throw new PosBusinessException("PERMISSION_DENIED", "You do not have permission to perform this action.", StatusCodes.Status403Forbidden);
		return user;
	}
}
