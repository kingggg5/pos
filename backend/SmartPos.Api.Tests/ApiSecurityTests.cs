using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SmartPos.Api.Dtos;

namespace SmartPos.Api.Tests;

public sealed class ApiSecurityTests : IAsyncLifetime
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"smart-pos-tests-{Guid.NewGuid():N}.db");
	private WebApplicationFactory<Program> _factory = null!;
	private HttpClient _client = null!;

	public Task InitializeAsync()
	{
		_factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
		{
			builder.UseEnvironment("Development");
			builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["UseSqlite"] = "true",
					["ConnectionStrings:Sqlite"] = $"Data Source={_databasePath};Pooling=False",
					["Cors:AllowedOrigins:0"] = "http://localhost"
				}));
		});
		_client = _factory.CreateClient();
		return Task.CompletedTask;
	}

	public async Task DisposeAsync()
	{
		_client.Dispose();
		await _factory.DisposeAsync();
		SqliteConnection.ClearAllPools();
		if (File.Exists(_databasePath))
			File.Delete(_databasePath);
	}

	[Fact]
	public async Task Login_works_with_fail_closed_filters_and_users_require_authentication()
	{
		var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto("owner@coffee.com", "password123"));
		var anonymousUsers = await _client.GetAsync("/api/users");

		Assert.Equal(HttpStatusCode.OK, login.StatusCode);
		using var payload = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
		var tenantId = payload.RootElement.GetProperty("tenantId").GetInt32();
		var token = payload.RootElement.GetProperty("token").GetString();
		Assert.True(tenantId > 0);
		Assert.Equal(HttpStatusCode.Unauthorized, anonymousUsers.StatusCode);

		using var mismatchRequest = new HttpRequestMessage(HttpMethod.Get, "/api/products");
		mismatchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		mismatchRequest.Headers.Add("X-Tenant-Id", (tenantId + 1).ToString());
		var mismatchResponse = await _client.SendAsync(mismatchRequest);
		Assert.Equal(HttpStatusCode.Forbidden, mismatchResponse.StatusCode);
	}

	[Fact]
	public async Task Register_store_is_atomic_and_new_owner_can_login()
	{
		var email = $"owner-{Guid.NewGuid():N}@example.com";
		var slug = $"store-{Guid.NewGuid():N}";
		var registration = await _client.PostAsJsonAsync("/api/auth/register-store", new RegisterStoreDto(
			"Integration Store",
			slug,
			email,
			"secure-password",
			"Integration Owner"));
		var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto(email, "secure-password"));

		Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
		Assert.Equal(HttpStatusCode.OK, login.StatusCode);
	}
}
