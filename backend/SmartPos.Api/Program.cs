using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartPos.Api.Data;
using SmartPos.Api.Infrastructure;
using SmartPos.Api.Realtime;
using SmartPos.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicyName = "SmartPosFrontendPolicy";

builder.WebHost.ConfigureKestrel(options =>
{
	options.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50MB
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

var signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
	throw new InvalidOperationException("Jwt:SigningKey must be configured and contain at least 32 characters.");

builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ICommerceService, CommerceService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IZReportService, ZReportService>();

var useSqliteEnvironment = Environment.GetEnvironmentVariable("USE_SQLITE");
var hasEnvironmentConnectionString = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
var useSqlite = string.Equals(useSqliteEnvironment, "true", StringComparison.OrdinalIgnoreCase) ||
	(!hasEnvironmentConnectionString && builder.Configuration.GetValue<bool>("UseSqlite"));

builder.Services.AddDbContext<SmartPosDbContext>(options =>
{
	if (useSqlite)
	{
		options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=smart_pos.db");
	}
	else
	{
		options.UseNpgsql(
			connectionString ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required when SQLite is disabled."),
			npgsqlOptions =>
			npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null));
	}
});

builder.Services.AddControllers()
	.AddJsonOptions(options =>
		options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(options =>
	{
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidIssuer = "SmartPos.Api",
			ValidateAudience = true,
			ValidAudience = "SmartPos.Web",
			ValidateLifetime = true,
			ValidateIssuerSigningKey = true,
			IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
			ClockSkew = TimeSpan.FromMinutes(1)
		};
		options.Events = new JwtBearerEvents
		{
			OnMessageReceived = context =>
			{
				var accessToken = context.Request.Query["access_token"];
				if (!string.IsNullOrEmpty(accessToken) &&
					context.HttpContext.Request.Path.StartsWithSegments("/hubs/pos"))
				{
					context.Token = accessToken;
				}
				return Task.CompletedTask;
			}
		};
	});

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
	var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
	if (allowedOrigins.Length == 0)
		throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one trusted frontend origin.");
	options.AddPolicy(CorsPolicyName, policy =>
		policy.WithOrigins(allowedOrigins)
			.AllowAnyHeader()
			.AllowAnyMethod()
			.AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseCors(CorsPolicyName);
app.Use(async (context, next) =>
{
	try
	{
		await next(context);
	}
	catch (PosBusinessException exception) when (!context.Response.HasStarted)
	{
		context.Response.StatusCode = exception.StatusCode;
		await context.Response.WriteAsJsonAsync(new
		{
			type = $"https://smart-pos.local/problems/{exception.Code.ToLowerInvariant()}",
			title = exception.Code,
			status = exception.StatusCode,
			detail = exception.Message,
			code = exception.Code
		});
	}
});
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHub<PosHub>("/hubs/pos");

using (var scope = app.Services.CreateScope())
{
	await DatabaseInitializer.InitializeAsync(scope.ServiceProvider);
}

app.Run();

public partial class Program;
