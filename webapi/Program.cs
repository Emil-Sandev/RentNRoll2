using RentNRoll.Data;
using RentNRoll.Data.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using RentNRoll.Services.Mapping;
using RentNRoll.Web.DTOs.Login;
using RentNRoll.Services.Token;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RentNRollDBContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
	.AddEntityFrameworkStores<RentNRollDBContext>()
	.AddDefaultTokenProviders();

builder.Services.AddSingleton<ITokenService, TokenService>();

builder.Services.AddCors(options =>
{
	options.AddPolicy("CorsPolicy",
		builder => builder
			.AllowAnyOrigin()
			.AllowAnyMethod()
			.AllowAnyHeader());
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
	options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
	{
		In = ParameterLocation.Header,
		Description = "Please enter 'Bearer [jwt]'",
		Name = "Authorization",
		Type = SecuritySchemeType.ApiKey
	});

	var scheme = new OpenApiSecurityScheme
	{
		Reference = new OpenApiReference
		{
			Type = ReferenceType.SecurityScheme,
			Id = "Bearer"
		}
	};

	options.AddSecurityRequirement(new OpenApiSecurityRequirement { { scheme, Array.Empty<string>() } });
});

using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddConsole());

var secret = builder.Configuration["JWT:Secret"] ?? throw new InvalidOperationException("Secret not configured");

builder.Services.AddAuthentication(options =>
{
	options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
	options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
	options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
	options.SaveToken = true;
	options.TokenValidationParameters = new TokenValidationParameters
	{
		ValidIssuer = builder.Configuration["JWT:ValidIssuer"],
		ValidAudience = builder.Configuration["JWT:ValidAudience"],
		IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
		ClockSkew = new TimeSpan(0, 0, 5)
	};
	options.Events = new JwtBearerEvents
	{
		OnChallenge = ctx => LogAttempt(ctx.Request.Headers, "OnChallenge"),
		OnTokenValidated = ctx => LogAttempt(ctx.Request.Headers, "OnTokenValidated")
	};
});

var app = builder.Build();

// custom mappings will happen only in DTO project
AutoMapperConfig.RegisterMappings(typeof(LoginDTO).Assembly);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseCors("CorsPolicy");

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();

Task LogAttempt(IHeaderDictionary headers, string eventType)
{
	var logger = loggerFactory.CreateLogger<Program>();

	var authorizationHeader = headers["Authorization"].FirstOrDefault();

	if (authorizationHeader is null)
		logger.LogInformation($"{eventType}. JWT not present");
	else
	{
		string jwtString = authorizationHeader.Substring("Bearer ".Length);

		var jwt = new JwtSecurityToken(jwtString);

		logger.LogInformation($"{eventType}. Expiration: {jwt.ValidTo.ToLongTimeString()}. System time: {DateTime.UtcNow.ToLongTimeString()}");
	}

	return Task.CompletedTask;
}