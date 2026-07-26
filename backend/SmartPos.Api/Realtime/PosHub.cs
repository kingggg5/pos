using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SmartPos.Api.Realtime;

[Authorize]
public sealed class PosHub : Hub
{
	public async Task JoinStoreGroup(string? _ = null)
	{
		var tenantSlug = Context.User?.FindFirst("tenant_slug")?.Value;
		if (string.IsNullOrWhiteSpace(tenantSlug))
			throw new HubException("Authenticated tenant is required.");
		await Groups.AddToGroupAsync(Context.ConnectionId, $"store-{tenantSlug}");
	}

	public async Task LeaveStoreGroup(string? _ = null)
	{
		var tenantSlug = Context.User?.FindFirst("tenant_slug")?.Value;
		if (!string.IsNullOrWhiteSpace(tenantSlug))
			await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"store-{tenantSlug}");
	}
}
