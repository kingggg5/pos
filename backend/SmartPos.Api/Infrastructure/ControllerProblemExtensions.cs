using Microsoft.AspNetCore.Mvc;
using SmartPos.Api.Services;

namespace SmartPos.Api.Infrastructure;

public static class ControllerProblemExtensions
{
	public static ObjectResult ToProblem(this ControllerBase controller, PosBusinessException exception)
	{
		var details = new ProblemDetails
		{
			Status = exception.StatusCode,
			Title = exception.Code,
			Detail = exception.Message,
			Type = $"https://smart-pos.local/problems/{exception.Code.ToLowerInvariant()}"
		};
		details.Extensions["code"] = exception.Code;
		return controller.StatusCode(exception.StatusCode, details);
	}
}
