namespace SmartPos.Api.Infrastructure;

public interface ITenantProvider
{
	int? CurrentTenantId { get; }
	void SetTenantId(int tenantId);
}

public sealed class TenantProvider : ITenantProvider
{
	private int? _currentTenantId;

	public int? CurrentTenantId => _currentTenantId;

	public void SetTenantId(int tenantId)
	{
		_currentTenantId = tenantId;
	}
}
