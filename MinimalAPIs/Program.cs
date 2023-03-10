global using Hangfire;
global using Hangfire.Common;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.IdentityModel.Tokens;
global using MinimalAPIs.Services;
global using System.IdentityModel.Tokens.Jwt;
global using System.Security.Cryptography.X509Certificates;
global using System.Text;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using MinimalAPIs.Handlers;
using MinimalAPIs.Models;

// Create the app builder.
var builder = WebApplication.CreateBuilder(args);

// Add configurations to the container.
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Add loggers to the container.
builder.Logging.AddJsonConsole();

// Parameters
var connectionString = builder.Configuration.GetConnectionString("connectionstring") ?? builder.Configuration["ConnectionString"]?.ToString() ?? "";          // from Secrets.json ?? from appsettings.json
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!));
var keyCert = new X509SecurityKey(new X509Certificate2(builder.Configuration["Certificate:Path"]!, builder.Configuration["Certificate:Password"]));
var keys = new List<SecurityKey> { key, keyCert };

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddDbContext<MinimalDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSwaggerGen(options =>
{
    options.SupportNonNullableReferenceTypes();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JSON Web Token based security",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
        {
            new OpenApiSecurityScheme
            {
                Name = "Bearer",
                In = ParameterLocation.Header,
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new List<string>()
        }
    });
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKeys = keys,
        TokenDecryptionKeys = new List<SecurityKey> { new EncryptingCredentials(key, JwtConstants.DirectKeyUseAlg, SecurityAlgorithms.Aes256CbcHmacSha512).Key },
        IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
        {
            return keys;
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<MyTokenHandler>();
builder.Services.AddHangfire(configuration => configuration.UseMemoryStorage()).AddHangfireServer();
JobStorage.Current = new MemoryStorage();

// Add the app to the container.
var app = builder.Build();

// Map the endpoints.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetService<MyTokenHandler>()?.RegisterAPIs(app);
}

// Configure the HTTP request pipeline.
app.UseHsts();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/Health");
app.UseHangfireDashboard();
app.UseExceptionHandler("/Error");
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.Use(async (context, next) =>
{
    await next();
    if (context.Response.StatusCode == StatusCodes.Status404NotFound) context.Response.Redirect("/swagger/index.html");
});

// Run the app.
app.Run();