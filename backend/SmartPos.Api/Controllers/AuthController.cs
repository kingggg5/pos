using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartPos.Api.Data;
using SmartPos.Api.Dtos;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Models;

namespace SmartPos.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
	SmartPosDbContext dbContext,
	ITenantProvider tenantProvider,
	IConfiguration configuration) : ControllerBase
{
	[HttpPost("register-store")]
	[AllowAnonymous]
	public async Task<ActionResult<AuthResponseDto>> RegisterStore(
		RegisterStoreDto request,
		CancellationToken cancellationToken)
	{
		var normalizedSlug = request.StoreSlug.ToLower().Trim();
		if (await dbContext.Tenants.AnyAsync(t => t.Slug == normalizedSlug, cancellationToken))
		{
			return BadRequest(new { message = "Store slug is already taken." });
		}

		var normalizedEmail = request.OwnerEmail.ToLower().Trim();
		if (await dbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == normalizedEmail, cancellationToken))
		{
			return BadRequest(new { message = "Owner email is already registered." });
		}

		var strategy = dbContext.Database.CreateExecutionStrategy();
		try
		{
			return await strategy.ExecuteAsync(async () =>
			{
				await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
				var tenant = new Tenant
				{
					Name = request.StoreName.Trim(),
					Slug = normalizedSlug,
					Plan = "Basic",
					IsActive = true
				};
				dbContext.Tenants.Add(tenant);
				await dbContext.SaveChangesAsync(cancellationToken);

				tenantProvider.SetTenantId(tenant.Id);
				var user = new User
				{
					TenantId = tenant.Id,
					Email = normalizedEmail,
					PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.OwnerPassword),
					FullName = request.OwnerFullName.Trim(),
					EmployeeCode = "OWNER-001",
					PositionTitle = "Store Owner",
					Role = UserRole.Owner,
					CanProcessCheckout = true,
					CanManageProducts = true,
					CanViewReports = true,
					CanManageUsers = true
				};
				dbContext.Users.Add(user);
				await dbContext.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);

				var token = GenerateJwtToken(user, tenant);
				return Ok(new AuthResponseDto(
					user.Id,
					user.Email,
					user.FullName,
					user.Role,
					tenant.Id,
					tenant.Name,
					tenant.Slug,
					token
				));
			});
		}
		catch (DbUpdateException)
		{
			return Conflict(new { message = "Store slug or owner email is already registered." });
		}
	}

	[HttpPost("login")]
	[AllowAnonymous]
	public async Task<ActionResult<AuthResponseDto>> Login(
		LoginDto request,
		CancellationToken cancellationToken)
	{
		var normalizedEmail = request.Email.ToLower().Trim();
		var user = await dbContext.Users
			.IgnoreQueryFilters()
			.Include(u => u.Tenant)
			.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

		if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
		{
			return Unauthorized(new { message = "Invalid email or password." });
		}

		if (user.Tenant == null || !user.Tenant.IsActive)
		{
			return BadRequest(new { message = "Store subscription is inactive." });
		}

		var token = GenerateJwtToken(user, user.Tenant);
		return Ok(new AuthResponseDto(
			user.Id,
			user.Email,
			user.FullName,
			user.Role,
			user.Tenant.Id,
			user.Tenant.Name,
			user.Tenant.Slug,
			token
		));
	}

	[HttpGet("me")]
	[Authorize]
	public async Task<ActionResult<AuthResponseDto>> GetCurrentUser(CancellationToken cancellationToken)
	{
		var emailClaim = User.FindFirst(ClaimTypes.Email)?.Value;

		if (string.IsNullOrEmpty(emailClaim))
		{
			return Unauthorized();
		}

		var user = await dbContext.Users
			.Include(u => u.Tenant)
			.FirstOrDefaultAsync(u => u.Email == emailClaim, cancellationToken);

		if (user == null || user.Tenant == null)
		{
			return Unauthorized();
		}

		return Ok(new AuthResponseDto(
			user.Id,
			user.Email,
			user.FullName,
			user.Role,
			user.Tenant.Id,
			user.Tenant.Name,
			user.Tenant.Slug,
			""
		));
	}

	private string GenerateJwtToken(User user, Tenant tenant)
	{
		var signingKey = configuration["Jwt:SigningKey"]
			?? throw new InvalidOperationException("Jwt signing key is not configured.");
		var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
		var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, user.Id.ToString()),
			new(ClaimTypes.Email, user.Email),
			new(ClaimTypes.Role, user.Role.ToString()),
			new("tenant_id", tenant.Id.ToString()),
			new("tenant_slug", tenant.Slug)
		};

		var tokenDescriptor = new JwtSecurityToken(
			issuer: "SmartPos.Api",
			audience: "SmartPos.Web",
			claims: claims,
			expires: DateTime.UtcNow.AddDays(7),
			signingCredentials: credentials);

		return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
	}
}
