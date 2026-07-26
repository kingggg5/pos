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
[Route("api/[controller]")]
[Authorize(Roles = "Owner,Manager")]
public class UsersController(
	SmartPosDbContext dbContext,
	IAuditLogService auditLogService,
	ICurrentUserService currentUserService) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<List<UserDto>>> GetUsers(CancellationToken cancellationToken)
	{
		await currentUserService.RequireAsync(
			User,
			value => value.CanManageUsers && value.Role is UserRole.Owner or UserRole.Manager,
			cancellationToken);
		var users = await dbContext.Users
			.AsNoTracking()
			.OrderBy(u => u.EmployeeCode)
			.ThenBy(u => u.FullName)
			.Select(u => new UserDto(
				u.Id,
				u.Email,
				u.FullName,
				u.EmployeeCode,
				u.PositionTitle,
				u.Role,
				u.CanProcessCheckout,
				u.CanManageProducts,
				u.CanViewReports,
				u.CanManageUsers,
				u.CreatedAt
			))
			.ToListAsync(cancellationToken);

		return Ok(users);
	}

	[HttpPost]
	public async Task<ActionResult<UserDto>> CreateStaff([FromBody] CreateStaffRequest request, CancellationToken cancellationToken)
	{
		var actor = await currentUserService.RequireAsync(
			User,
			value => value.CanManageUsers && value.Role is UserRole.Owner or UserRole.Manager,
			cancellationToken);
		if (actor.Role == UserRole.Manager &&
			(request.Role == UserRole.Owner || GrantsBeyondActor(actor, request.CanProcessCheckout, request.CanManageProducts, request.CanViewReports, request.CanManageUsers)))
			return Forbid();

		var normalizedEmail = request.Email.Trim().ToLowerInvariant();
		if (await dbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == normalizedEmail, cancellationToken))
		{
			return Conflict(new { message = "User with this email already exists." });
		}

		var user = new User
		{
			Email = normalizedEmail,
			PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
			FullName = request.FullName.Trim(),
			EmployeeCode = string.IsNullOrWhiteSpace(request.EmployeeCode) ? $"STAFF-{Guid.NewGuid():N}"[..12] : request.EmployeeCode.Trim(),
			PositionTitle = string.IsNullOrWhiteSpace(request.PositionTitle) ? "Staff Member" : request.PositionTitle.Trim(),
			Role = request.Role,
			CanProcessCheckout = request.CanProcessCheckout,
			CanManageProducts = request.CanManageProducts,
			CanViewReports = request.CanViewReports,
			CanManageUsers = request.CanManageUsers,
			CreatedAt = DateTime.UtcNow
		};

		dbContext.Users.Add(user);
		await dbContext.SaveChangesAsync(cancellationToken);

		await auditLogService.LogAsync("STAFF_CREATED", "Staff", $"Added staff member {user.FullName} ({user.EmployeeCode}) as {user.PositionTitle}", cancellationToken: cancellationToken);

		var dto = new UserDto(
			user.Id,
			user.Email,
			user.FullName,
			user.EmployeeCode,
			user.PositionTitle,
			user.Role,
			user.CanProcessCheckout,
			user.CanManageProducts,
			user.CanViewReports,
			user.CanManageUsers,
			user.CreatedAt
		);

		return Ok(dto);
	}

	[HttpPut("{id:int}")]
	public async Task<ActionResult<UserDto>> UpdateStaff(int id, [FromBody] UpdateStaffRequest request, CancellationToken cancellationToken)
	{
		var actor = await currentUserService.RequireAsync(
			User,
			value => value.CanManageUsers && value.Role is UserRole.Owner or UserRole.Manager,
			cancellationToken);
		var user = await dbContext.Users.FindAsync([id], cancellationToken);
		if (user == null) return NotFound();
		if (user.Role == UserRole.Owner && actor.Id != user.Id)
			return Forbid();
		if (actor.Role == UserRole.Manager &&
			(request.Role == UserRole.Owner || user.Role == UserRole.Owner ||
			 GrantsBeyondActor(actor, request.CanProcessCheckout, request.CanManageProducts, request.CanViewReports, request.CanManageUsers)))
			return Forbid();
		if (actor.Id == user.Id &&
			(request.Role != user.Role ||
			 request.CanManageUsers != user.CanManageUsers ||
			 request.CanManageProducts != user.CanManageProducts ||
			 request.CanProcessCheckout != user.CanProcessCheckout ||
			 request.CanViewReports != user.CanViewReports))
			return BadRequest(new { message = "Users cannot change their own role or permissions." });
		if (user.Role == UserRole.Owner && request.Role != UserRole.Owner &&
			await dbContext.Users.CountAsync(value => value.Role == UserRole.Owner, cancellationToken) <= 1)
			return Conflict(new { message = "The last owner cannot be demoted." });

		user.FullName = request.FullName;
		user.EmployeeCode = request.EmployeeCode;
		user.PositionTitle = request.PositionTitle;
		user.Role = request.Role;
		user.CanProcessCheckout = request.CanProcessCheckout;
		user.CanManageProducts = request.CanManageProducts;
		user.CanViewReports = request.CanViewReports;
		user.CanManageUsers = request.CanManageUsers;

		await dbContext.SaveChangesAsync(cancellationToken);

		await auditLogService.LogAsync("STAFF_UPDATED", "Staff", $"Updated staff details for {user.FullName} ({user.EmployeeCode})", cancellationToken: cancellationToken);

		var dto = new UserDto(
			user.Id,
			user.Email,
			user.FullName,
			user.EmployeeCode,
			user.PositionTitle,
			user.Role,
			user.CanProcessCheckout,
			user.CanManageProducts,
			user.CanViewReports,
			user.CanManageUsers,
			user.CreatedAt
		);

		return Ok(dto);
	}

	[HttpDelete("{id:int}")]
	public async Task<IActionResult> DeleteStaff(int id, CancellationToken cancellationToken)
	{
		var actor = await currentUserService.RequireAsync(
			User,
			value => value.CanManageUsers && value.Role is UserRole.Owner or UserRole.Manager,
			cancellationToken);
		var user = await dbContext.Users.FindAsync([id], cancellationToken);
		if (user == null) return NotFound();
		if (actor.Id == user.Id)
			return BadRequest(new { message = "Users cannot delete their own account." });
		if (user.Role == UserRole.Owner)
			return Forbid();

		dbContext.Users.Remove(user);
		await dbContext.SaveChangesAsync(cancellationToken);

		await auditLogService.LogAsync("STAFF_DELETED", "Staff", $"Removed staff user {user.FullName} ({user.EmployeeCode})", cancellationToken: cancellationToken);

		return NoContent();
	}

	private static bool GrantsBeyondActor(
		User actor,
		bool canProcessCheckout,
		bool canManageProducts,
		bool canViewReports,
		bool canManageUsers) =>
		(canProcessCheckout && !actor.CanProcessCheckout) ||
		(canManageProducts && !actor.CanManageProducts) ||
		(canViewReports && !actor.CanViewReports) ||
		(canManageUsers && !actor.CanManageUsers);
}
